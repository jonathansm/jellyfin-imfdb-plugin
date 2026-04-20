using System.Reflection;
using Jellyfin.Plugin.Imfdb.Models;
using Jellyfin.Plugin.Imfdb.Services;
using Jellyfin.Plugin.Imfdb.Web;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="imfdbClient">IMFDB lookup client.</param>
    public ImfdbController(ILibraryManager libraryManager, IImfdbClient imfdbClient)
    {
        _libraryManager = libraryManager;
        _imfdbClient = imfdbClient;
    }

    /// <summary>
    /// Looks up firearms for a Jellyfin item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Grouped firearm information.</returns>
    [HttpGet("Lookup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImfdbLookupResult>> Lookup([FromQuery] Guid itemId, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableLookups == false)
        {
            return Ok(new ImfdbLookupResult(itemId, null, string.Empty, null, null, null, Array.Empty<FirearmResult>()));
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        item.ProviderIds.TryGetValue("Imdb", out var imdbId);
        var (sourceTitle, sourceUrl, firearms) = await _imfdbClient
            .LookupAsync(item.Name, item.ProductionYear, imdbId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new ImfdbLookupResult(
            itemId,
            imdbId,
            item.Name,
            item.ProductionYear,
            sourceTitle,
            sourceUrl,
            firearms));
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Status()
    {
        return Ok(new
        {
            PluginEnabled = Plugin.Instance?.Configuration.EnableLookups != false,
            WebUiInjectionEnabled = Plugin.Instance?.Configuration.EnableWebUiInjection != false,
            FileTransformationRegistered = FileTransformationRegistrationService.IsRegistered,
            FileTransformationStatus = FileTransformationRegistrationService.LastStatus
        });
    }
}
