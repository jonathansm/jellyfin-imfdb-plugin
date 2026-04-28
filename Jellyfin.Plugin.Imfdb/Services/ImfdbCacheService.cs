using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.Imfdb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// File-backed IMFDB cache stored in plugin-managed storage.
/// </summary>
public sealed partial class ImfdbCacheService : IImfdbCacheService
{
    private const string CacheFolderName = "cache";
    private const string CacheFileName = "imfdb.json";
    private const string MetadataFolderName = "metadata";
    private const string MovieFolderName = "movies";
    private const string TvFolderName = "tv";
    private const string ImageFolderName = "images";
    private const int MaxConcurrentImageDownloads = 4;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ImageLocks = new(StringComparer.Ordinal);
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
                    await CacheFirearmsAsync(result.Firearms, cancellationToken).ConfigureAwait(false));
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
        var imageFolder = GetImageFolderPath();
        if (imageFolder is null || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var relativeImagePath = fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativeImagePath) || relativeImagePath.Split(Path.DirectorySeparatorChar).Any(static part => part is ".." or ""))
        {
            return null;
        }

        var imagePath = Path.GetFullPath(Path.Combine(imageFolder, relativeImagePath));
        var fullImageFolder = Path.GetFullPath(imageFolder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRelativeImagePath = relativeImagePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!imagePath.StartsWith(fullImageFolder + Path.DirectorySeparatorChar, comparison) ||
            !CachedImageBelongsToItem(item, normalizedRelativeImagePath) ||
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
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            return null;
        }

        return Path.Combine(dataFolderPath, CacheFolderName, MetadataFolderName, GetMetadataRelativePath(item));
    }

    private static string? GetCacheFilePath(BaseItem item)
    {
        var cacheFolder = GetCacheFolderPath(item);
        return cacheFolder is null ? null : Path.Combine(cacheFolder, CacheFileName);
    }

    private static string? GetImageFolderPath()
    {
        var dataFolderPath = Plugin.Instance?.DataFolderPath;
        return string.IsNullOrWhiteSpace(dataFolderPath)
            ? null
            : Path.Combine(dataFolderPath, CacheFolderName, ImageFolderName);
    }

    private static string GetMetadataRelativePath(BaseItem item)
    {
        if (item is Movie)
        {
            return Path.Combine(MovieFolderName, item.Id.ToString("N"));
        }

        if (item is Series series)
        {
            return Path.Combine(TvFolderName, series.Id.ToString("N"), "series");
        }

        if (item is Season season)
        {
            var seriesId = season.Series?.Id ?? season.SeriesId;
            return Path.Combine(TvFolderName, GetGuidKey(seriesId, season.Id), "seasons", season.Id.ToString("N"));
        }

        if (item is Episode episode)
        {
            return Path.Combine(
                TvFolderName,
                GetGuidKey(episode.SeriesId, episode.Id),
                "seasons",
                GetGuidKey(episode.SeasonId, episode.Id));
        }

        return Path.Combine("items", item.Id.ToString("N"));
    }

    private static string GetGuidKey(Guid id, Guid fallback)
    {
        return (id == Guid.Empty ? fallback : id).ToString("N");
    }

    private async Task<IReadOnlyList<CachedFirearmResult>> CacheFirearmsAsync(
        IReadOnlyList<FirearmResult> firearms,
        CancellationToken cancellationToken)
    {
        var imageFolder = GetImageFolderPath();
        if (imageFolder is null)
        {
            return firearms.Select(static firearm => new CachedFirearmResult(
                firearm.Name,
                firearm.Url,
                firearm.SourceSectionUrl,
                firearm.ImageUrl,
                null,
                firearm.Summary,
                firearm.Details)).ToArray();
        }

        using var throttler = new SemaphoreSlim(MaxConcurrentImageDownloads);
        var cacheTasks = firearms.Select(async firearm =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var imageFileName = await CacheImageAsync(imageFolder, firearm, cancellationToken).ConfigureAwait(false);
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

    private async Task<string?> CacheImageAsync(
        string imageFolder,
        FirearmResult firearm,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firearm.ImageUrl) ||
            !Uri.TryCreate(firearm.ImageUrl, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var imageHash = Hash(imageUri.ToString());
        var imageLock = ImageLocks.GetOrAdd(imageHash, static _ => new SemaphoreSlim(1, 1));
        await imageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachedImagePath = TryFindCachedImageRelativePath(imageFolder, imageHash);
            if (cachedImagePath is not null)
            {
                return cachedImagePath;
            }

            using var response = await HttpClient.GetAsync(imageUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var extension = GetImageExtension(response.Content.Headers.ContentType?.MediaType, imageUri);
            var relativeImagePath = GetImageRelativePath(imageHash, extension);
            var filePath = Path.Combine(imageFolder, relativeImagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var tempFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = File.Create(tempFilePath))
                {
                    await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                File.Move(tempFilePath, filePath, false);
            }
            catch (IOException) when (File.Exists(filePath))
            {
                TryDeleteFile(tempFilePath);
            }
            catch
            {
                TryDeleteFile(tempFilePath);
                throw;
            }

            return relativeImagePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Unable to cache IMFDB image {ImageUrl}", firearm.ImageUrl);
            return null;
        }
        finally
        {
            imageLock.Release();
        }
    }

    private static ImfdbLookupResult ToLookupResult(BaseItem item, CachedImfdbLookup cache, bool isCached)
    {
        var refreshRecommended = false;
        var firearms = cache.Firearms.Select(firearm =>
        {
            var cachedImageUrl = GetCachedImageUrlIfPresent(item.Id, firearm.CachedImageFileName);
            if (!string.IsNullOrWhiteSpace(firearm.CachedImageFileName) && cachedImageUrl is null)
            {
                refreshRecommended = true;
            }

            return new FirearmResult(
                firearm.Name,
                firearm.Url,
                firearm.SourceSectionUrl,
                cachedImageUrl ?? firearm.OriginalImageUrl,
                firearm.Summary,
                firearm.Details);
        }).ToArray();

        return new ImfdbLookupResult(
            cache.ItemId,
            cache.ImdbId,
            cache.QueryTitle,
            cache.Year,
            cache.SourceTitle,
            cache.SourceUrl,
            cache.ImfdbUrl,
            firearms,
            isCached,
            cache.CachedAt,
            refreshRecommended);
    }

    private static string? GetCachedImageUrlIfPresent(Guid itemId, string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName) || !CachedImageExists(fileName)
            ? null
            : $"/Imfdb/Image?itemId={Uri.EscapeDataString(itemId.ToString())}&fileName={Uri.EscapeDataString(fileName)}";
    }

    private static bool CachedImageExists(string fileName)
    {
        var imageFolder = GetImageFolderPath();
        if (imageFolder is null)
        {
            return false;
        }

        var relativeImagePath = fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativeImagePath) || relativeImagePath.Split(Path.DirectorySeparatorChar).Any(static part => part is ".." or ""))
        {
            return false;
        }

        var imagePath = Path.GetFullPath(Path.Combine(imageFolder, relativeImagePath));
        var fullImageFolder = Path.GetFullPath(imageFolder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return imagePath.StartsWith(fullImageFolder + Path.DirectorySeparatorChar, comparison) && File.Exists(imagePath);
    }

    private static bool CachedImageBelongsToItem(BaseItem item, string relativeImagePath)
    {
        var cacheFilePath = GetCacheFilePath(item);
        if (cacheFilePath is null || !File.Exists(cacheFilePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(cacheFilePath);
            var cache = JsonSerializer.Deserialize<CachedImfdbLookup>(stream, JsonOptions);
            return cache?.Firearms.Any(firearm => string.Equals(
                firearm.CachedImageFileName,
                relativeImagePath,
                StringComparison.Ordinal)) == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string? TryFindCachedImageRelativePath(string imageFolder, string imageHash)
    {
        var shardFolder = Path.Combine(imageFolder, GetImageShard(imageHash));
        if (!Directory.Exists(shardFolder))
        {
            return null;
        }

        var existingImagePath = Directory
            .EnumerateFiles(shardFolder, imageHash + ".*")
            .FirstOrDefault(static path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        return existingImagePath is null
            ? null
            : Path.GetRelativePath(imageFolder, existingImagePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string GetImageRelativePath(string imageHash, string extension)
    {
        return Path.Combine(GetImageShard(imageHash), imageHash + extension).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string GetImageShard(string imageHash)
    {
        return Path.Combine(imageHash[..2], imageHash[2..4]);
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
