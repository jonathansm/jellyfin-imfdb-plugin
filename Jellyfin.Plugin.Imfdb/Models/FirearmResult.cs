namespace Jellyfin.Plugin.Imfdb.Models;

/// <summary>
/// Firearm grouped for the client card row.
/// </summary>
/// <param name="Name">Firearm name.</param>
/// <param name="Url">IMFDB or IMFDB Browser firearm URL.</param>
/// <param name="SourceSectionUrl">Matched title's IMFDB section URL for this firearm.</param>
/// <param name="ImageUrl">Best available firearm image URL.</param>
/// <param name="Summary">Short summary for the card.</param>
/// <param name="Details">Firearm details for the expanded view.</param>
public sealed record FirearmResult(
    string Name,
    string? Url,
    string? SourceSectionUrl,
    string? ImageUrl,
    string Summary,
    string? Details);
