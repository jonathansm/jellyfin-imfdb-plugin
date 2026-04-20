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
public sealed record ImfdbLookupResult(
    Guid ItemId,
    string? ImdbId,
    string QueryTitle,
    int? Year,
    string? SourceTitle,
    string? SourceUrl,
    string? ImfdbUrl,
    IReadOnlyList<FirearmResult> Firearms);
