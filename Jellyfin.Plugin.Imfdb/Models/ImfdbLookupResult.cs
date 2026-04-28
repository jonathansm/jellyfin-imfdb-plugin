namespace Jellyfin.Plugin.Imfdb.Models;

/// <summary>
/// API response for an item lookup.
/// </summary>
/// <param name="ItemId">Jellyfin item id.</param>
/// <param name="ImdbId">IMDb provider id, when present.</param>
/// <param name="QueryTitle">Title searched.</param>
/// <param name="Year">Production year searched.</param>
/// <param name="SourceTitle">Matched IMFDB title.</param>
/// <param name="SourceUrl">Matched source URL.</param>
/// <param name="ImfdbUrl">Matched title's main IMFDB wiki URL.</param>
/// <param name="Firearms">Grouped firearm results.</param>
/// <param name="IsCached">Whether the result was read from a local cache.</param>
/// <param name="CachedAt">When the local cache was written, when available.</param>
/// <param name="RefreshRecommended">Whether the client should refresh the cached result from IMFDB.</param>
public sealed record ImfdbLookupResult(
    Guid ItemId,
    string? ImdbId,
    string QueryTitle,
    int? Year,
    string? SourceTitle,
    string? SourceUrl,
    string? ImfdbUrl,
    IReadOnlyList<FirearmResult> Firearms,
    bool IsCached = false,
    DateTimeOffset? CachedAt = null,
    bool RefreshRecommended = false);
