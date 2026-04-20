using Jellyfin.Plugin.Imfdb.Models;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// Looks up firearms from IMFDB data.
/// </summary>
public interface IImfdbClient
{
    /// <summary>
    /// Searches IMFDB for a movie or series.
    /// </summary>
    /// <param name="title">Media title.</param>
    /// <param name="year">Production year.</param>
    /// <param name="imdbId">IMDb id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matched source title, source URL, and firearms.</returns>
    Task<(string? SourceTitle, string? SourceUrl, IReadOnlyList<FirearmResult> Firearms)> LookupAsync(
        string title,
        int? year,
        string? imdbId,
        CancellationToken cancellationToken);
}

