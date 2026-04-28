using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Imfdb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// File-backed IMFDB cache stored beside media files.
/// </summary>
public sealed partial class ImfdbCacheService : IImfdbCacheService
{
    private const string CacheFolderName = "firearms";
    private const string CacheFileName = "imfdb.json";
    private const string IgnoreFileName = ".ignore";
    private const int MaxConcurrentImageDownloads = 4;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheLocks = new(StringComparer.Ordinal);
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<ImfdbCacheService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbCacheService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ImfdbCacheService(ILogger<ImfdbCacheService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImfdbLookupResult?> ReadAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var cacheFilePath = GetCacheFilePath(item);
        if (cacheFilePath is null || !File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            var cacheFolder = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheFolder))
            {
                await WriteIgnoreFileAsync(cacheFolder, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = File.OpenRead(cacheFilePath);
            var cache = await JsonSerializer.DeserializeAsync<CachedImfdbLookup>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (cache is null)
            {
                return null;
            }

            _logger.LogInformation(
                "IMFDB cache read for {ItemName} from {CacheFilePath}; cached at {CachedAt}",
                item.Name,
                cacheFilePath,
                cache.CachedAt);
            return ToLookupResult(item, cache, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Unable to read IMFDB cache for {ItemName}", item.Name);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ImfdbLookupResult> WriteAsync(BaseItem item, ImfdbLookupResult result, CancellationToken cancellationToken)
    {
        var cacheFolder = GetCacheFolderPath(item);
        if (cacheFolder is null)
        {
            return result;
        }

        var cacheFilePath = Path.Combine(cacheFolder, CacheFileName);
        try
        {
            var cacheLock = CacheLocks.GetOrAdd(cacheFilePath, static _ => new SemaphoreSlim(1, 1));
            await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(cacheFolder);
                await WriteIgnoreFileAsync(cacheFolder, cancellationToken).ConfigureAwait(false);
                var cache = CreateCache(
                    result,
                    result.Firearms.Select(static firearm => new CachedFirearmResult(
                        firearm.Name,
                        firearm.Url,
                        firearm.SourceSectionUrl,
                        firearm.ImageUrl,
                        null,
                        firearm.Summary,
                        firearm.Details)).ToArray());

                await WriteCacheFileAsync(cacheFilePath, cache, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "IMFDB cache metadata written for {ItemName} to {CacheFilePath} with {FirearmCount} firearm sections",
                    item.Name,
                    cacheFilePath,
                    cache.Firearms.Count);

                cache = CreateCache(
                    result,
                    await CacheFirearmsAsync(cacheFolder, result.Firearms, cancellationToken).ConfigureAwait(false));
                await WriteCacheFileAsync(cacheFilePath, cache, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "IMFDB cache image refresh completed for {ItemName}: {CachedImageCount}/{FirearmCount} images cached",
                    item.Name,
                    cache.Firearms.Count(static firearm => !string.IsNullOrWhiteSpace(firearm.CachedImageFileName)),
                    cache.Firearms.Count);
                return ToLookupResult(item, cache, false);
            }
            finally
            {
                cacheLock.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Unable to write IMFDB cache for {ItemName}", item.Name);
            return result;
        }
    }

    private static CachedImfdbLookup CreateCache(ImfdbLookupResult result, IReadOnlyList<CachedFirearmResult> firearms)
    {
        return new CachedImfdbLookup(
                1,
                DateTimeOffset.UtcNow,
                result.ItemId,
                result.ImdbId,
                result.QueryTitle,
                result.Year,
                result.SourceTitle,
                result.SourceUrl,
                result.ImfdbUrl,
                firearms);
    }

    private static async Task WriteCacheFileAsync(string cacheFilePath, CachedImfdbLookup cache, CancellationToken cancellationToken)
    {
        var tempFilePath = $"{cacheFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFilePath, cacheFilePath, true);
        }
        catch
        {
            TryDeleteFile(tempFilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public string? GetImagePath(BaseItem item, string fileName)
    {
        var cacheFolder = GetCacheFolderPath(item);
        if (cacheFolder is null || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
        {
            return null;
        }

        var imagePath = Path.GetFullPath(Path.Combine(cacheFolder, safeFileName));
        var fullCacheFolder = Path.GetFullPath(cacheFolder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!imagePath.StartsWith(fullCacheFolder + Path.DirectorySeparatorChar, comparison) ||
            !File.Exists(imagePath))
        {
            return null;
        }

        return imagePath;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.Imfdb/0.1");
        return client;
    }

    private static string? GetCacheFolderPath(BaseItem item)
    {
        var mediaFolder = GetMediaFolderPath(item);
        return mediaFolder is null ? null : Path.Combine(mediaFolder, CacheFolderName);
    }

    private static string? GetCacheFilePath(BaseItem item)
    {
        var cacheFolder = GetCacheFolderPath(item);
        return cacheFolder is null ? null : Path.Combine(cacheFolder, CacheFileName);
    }

    private static string? GetMediaFolderPath(BaseItem item)
    {
        if (item is Season season)
        {
            return GetDirectoryFromPath(season.Path) ?? GetItemDirectory(season) ?? GetItemDirectory(season.Series);
        }

        if (item is Episode episode)
        {
            return GetItemDirectory(episode) ?? GetItemDirectory(episode.Series);
        }

        return GetItemDirectory(item);
    }

    private static string? GetDirectoryFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Directory.Exists(path) ? path : Path.GetDirectoryName(path);
    }

    private static string? GetItemDirectory(BaseItem? item)
    {
        if (item is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return item.ContainingFolderPath;
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return null;
        }

        return GetDirectoryFromPath(item.Path);
    }

    private async Task<IReadOnlyList<CachedFirearmResult>> CacheFirearmsAsync(
        string cacheFolder,
        IReadOnlyList<FirearmResult> firearms,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(MaxConcurrentImageDownloads);
        var cacheTasks = firearms.Select(async (firearm, index) =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var imageFileName = await CacheImageAsync(cacheFolder, firearm, index, cancellationToken).ConfigureAwait(false);
                return new CachedFirearmResult(
                    firearm.Name,
                    firearm.Url,
                    firearm.SourceSectionUrl,
                    firearm.ImageUrl,
                    imageFileName,
                    firearm.Summary,
                    firearm.Details);
            }
            finally
            {
                throttler.Release();
            }
        });

        return await Task.WhenAll(cacheTasks).ConfigureAwait(false);
    }

    private static async Task WriteIgnoreFileAsync(string cacheFolder, CancellationToken cancellationToken)
    {
        var ignoreFilePath = Path.Combine(cacheFolder, IgnoreFileName);
        if (File.Exists(ignoreFilePath))
        {
            return;
        }

        await File.WriteAllTextAsync(ignoreFilePath, "Jellyfin IMFDB cache folder. Ignore during library scans.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> CacheImageAsync(
        string cacheFolder,
        FirearmResult firearm,
        int index,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firearm.ImageUrl) ||
            !Uri.TryCreate(firearm.ImageUrl, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var response = await HttpClient.GetAsync(imageUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var extension = GetImageExtension(response.Content.Headers.ContentType?.MediaType, imageUri);
            var fileName = $"{index + 1:00}-{Slugify(firearm.Name)}-{Hash(imageUri.ToString())}{extension}";
            var filePath = Path.Combine(cacheFolder, fileName);
            await using var stream = File.Create(filePath);
            await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            return fileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Unable to cache IMFDB image {ImageUrl}", firearm.ImageUrl);
            return null;
        }
    }

    private static ImfdbLookupResult ToLookupResult(BaseItem item, CachedImfdbLookup cache, bool isCached)
    {
        return new ImfdbLookupResult(
            cache.ItemId,
            cache.ImdbId,
            cache.QueryTitle,
            cache.Year,
            cache.SourceTitle,
            cache.SourceUrl,
            cache.ImfdbUrl,
            cache.Firearms.Select(firearm => new FirearmResult(
                firearm.Name,
                firearm.Url,
                firearm.SourceSectionUrl,
                GetCachedImageUrl(item.Id, firearm.CachedImageFileName) ?? firearm.OriginalImageUrl,
                firearm.Summary,
                firearm.Details)).ToArray(),
            isCached,
            cache.CachedAt);
    }

    private static string? GetCachedImageUrl(Guid itemId, string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : $"/Imfdb/Image?itemId={Uri.EscapeDataString(itemId.ToString())}&fileName={Uri.EscapeDataString(fileName)}";
    }

    private static string GetImageExtension(string? mediaType, Uri imageUri)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => Path.GetExtension(imageUri.AbsolutePath) is { Length: > 0 } extension ? extension : ".jpg"
        };
    }

    private static string Slugify(string value)
    {
        var slug = NonFileNameRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "firearm" : slug;
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonFileNameRegex();

    private sealed record CachedImfdbLookup(
        int Version,
        DateTimeOffset CachedAt,
        Guid ItemId,
        string? ImdbId,
        string QueryTitle,
        int? Year,
        string? SourceTitle,
        string? SourceUrl,
        string? ImfdbUrl,
        IReadOnlyList<CachedFirearmResult> Firearms);

    private sealed record CachedFirearmResult(
        string Name,
        string? Url,
        string? SourceSectionUrl,
        string? OriginalImageUrl,
        string? CachedImageFileName,
        string Summary,
        string? Details);
}
