using System.Reflection;
using Jellyfin.Plugin.Imfdb.Models;
using Jellyfin.Plugin.Imfdb.Services;
using Jellyfin.Plugin.Imfdb.Web;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Api;

/// <summary>
/// IMFDB API endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("Imfdb")]
public class ImfdbController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IImfdbClient _imfdbClient;
    private readonly IImfdbCacheService _cacheService;
    private readonly ILogger<ImfdbController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="imfdbClient">IMFDB lookup client.</param>
    /// <param name="cacheService">IMFDB cache service.</param>
    /// <param name="logger">Logger.</param>
    public ImfdbController(
        ILibraryManager libraryManager,
        IImfdbClient imfdbClient,
        IImfdbCacheService cacheService,
        ILogger<ImfdbController> logger)
    {
        _libraryManager = libraryManager;
        _imfdbClient = imfdbClient;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Looks up firearms for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="refresh">Whether to bypass the local cache and refresh from IMFDB.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Grouped firearm information.</returns>
    [HttpGet("Lookup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImfdbLookupResult>> Lookup(
        [FromQuery] Guid itemId,
        [FromQuery] bool refresh,
        CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableLookups == false)
        {
            return Ok(new ImfdbLookupResult(itemId, null, string.Empty, null, null, null, null, Array.Empty<FirearmResult>()));
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        item.ProviderIds.TryGetValue("Imdb", out var imdbId);
        if (!refresh && Plugin.Instance?.Configuration.EnableCaching != false)
        {
            var cachedResult = await _cacheService.ReadAsync(item, cancellationToken).ConfigureAwait(false);
            if (cachedResult is not null)
            {
                var refreshRecommended = IsCacheRefreshRecommended(cachedResult.CachedAt);
                cachedResult = cachedResult with
                {
                    RefreshRecommended = refreshRecommended
                };

                _logger.LogInformation(
                    "IMFDB lookup served from cache for {Title} ({Year}) with {FirearmCount} firearm sections; refresh recommended: {RefreshRecommended}",
                    item.Name,
                    item.ProductionYear,
                    cachedResult.Firearms.Count,
                    refreshRecommended);
                return Ok(cachedResult);
            }

            _logger.LogInformation("IMFDB cache miss for {Title} ({Year})", item.Name, item.ProductionYear);
        }
        else if (refresh)
        {
            _logger.LogInformation("IMFDB refresh requested for {Title} ({Year})", item.Name, item.ProductionYear);
        }
        else
        {
            _logger.LogInformation("IMFDB cache disabled for {Title} ({Year})", item.Name, item.ProductionYear);
        }

        var (sourceTitle, sourceUrl, firearms) = await _imfdbClient
            .LookupAsync(item.Name, item.ProductionYear, cancellationToken)
            .ConfigureAwait(false);

        var result = new ImfdbLookupResult(
            itemId,
            imdbId,
            item.Name,
            item.ProductionYear,
            sourceTitle,
            sourceUrl,
            firearms.Select(static firearm => firearm.SourceSectionUrl)
                .FirstOrDefault(static url => !string.IsNullOrWhiteSpace(url))?
                .Split('#')[0],
            firearms);

        if (Plugin.Instance?.Configuration.EnableCaching != false)
        {
            _logger.LogInformation(
                "IMFDB live lookup returned {FirearmCount} firearm sections for {Title} ({Year}); cache write queued",
                result.Firearms.Count,
                item.Name,
                item.ProductionYear);
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await _cacheService.WriteAsync(item, result, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unable to write IMFDB cache in the background for {ItemName}", item.Name);
                    }
                },
                CancellationToken.None);
        }

        return Ok(result);
    }

    /// <summary>
    /// Returns a cached IMFDB image for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="fileName">Cached image file name.</param>
    /// <returns>Cached image file.</returns>
    [HttpGet("Image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Image([FromQuery] Guid itemId, [FromQuery] string fileName)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var imagePath = _cacheService.GetImagePath(item, fileName);
        if (imagePath is null)
        {
            return NotFound();
        }

        return PhysicalFile(imagePath, GetImageContentType(imagePath));
    }

    /// <summary>
    /// Returns the Jellyfin Web firearm row script.
    /// </summary>
    /// <returns>JavaScript content.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult ClientScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Jellyfin.Plugin.Imfdb.Web.imfdbClient.js";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "application/javascript");
    }

    /// <summary>
    /// Returns basic plugin diagnostics.
    /// </summary>
    /// <returns>Plugin diagnostics.</returns>
    [HttpGet("Status")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Status()
    {
        return Ok(new
        {
            PluginEnabled = Plugin.Instance?.Configuration.EnableLookups != false,
            WebUiInjectionEnabled = Plugin.Instance?.Configuration.EnableWebUiInjection != false,
            CachingEnabled = Plugin.Instance?.Configuration.EnableCaching != false,
            CacheRefreshIntervalHours = Math.Max(0, Plugin.Instance?.Configuration.CacheRefreshIntervalHours ?? 24),
            FileTransformationRegistered = FileTransformationRegistrationService.IsRegistered,
            FileTransformationStatus = FileTransformationRegistrationService.LastStatus
        });
    }

    private static bool IsCacheRefreshRecommended(DateTimeOffset? cachedAt)
    {
        var intervalHours = Math.Max(0, Plugin.Instance?.Configuration.CacheRefreshIntervalHours ?? 24);
        if (intervalHours == 0)
        {
            return true;
        }

        return cachedAt is null || DateTimeOffset.UtcNow - cachedAt.Value >= TimeSpan.FromHours(intervalHours);
    }

    private static string GetImageContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }
}
