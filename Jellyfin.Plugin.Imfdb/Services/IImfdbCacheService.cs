using Jellyfin.Plugin.Imfdb.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// Reads and writes IMFDB lookup caches beside media files.
/// </summary>
public interface IImfdbCacheService
{
    /// <summary>
    /// Reads cached lookup results for an item.
    /// </summary>
    /// <param name="item">Jellyfin item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached lookup results, when available.</returns>
    Task<ImfdbLookupResult?> ReadAsync(BaseItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Writes lookup results and associated images to the item cache folder.
    /// </summary>
    /// <param name="item">Jellyfin item.</param>
    /// <param name="result">Lookup result to cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached lookup result with image URLs pointing at cached images.</returns>
    Task<ImfdbLookupResult> WriteAsync(BaseItem item, ImfdbLookupResult result, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the path to a cached image file.
    /// </summary>
    /// <param name="item">Jellyfin item.</param>
    /// <param name="fileName">Cached image file name.</param>
    /// <returns>Cached image path, when valid and present.</returns>
    string? GetImagePath(BaseItem item, string fileName);
}
