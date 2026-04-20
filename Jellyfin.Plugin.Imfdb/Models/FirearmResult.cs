namespace Jellyfin.Plugin.Imfdb.Models;

/// <summary>
/// Firearm grouped for the client card row.
/// </summary>
/// <param name="Name">Firearm name.</param>
/// <param name="Url">IMFDB or IMFDB Browser firearm URL.</param>
/// <param name="ImageUrl">Best available firearm image URL.</param>
/// <param name="Summary">Short summary for the card.</param>
/// <param name="Details">Firearm details for the expanded view.</param>
/// <param name="DetailSourceUrl">Source URL for the firearm details.</param>
/// <param name="Appearances">Known appearances in the title.</param>
public sealed record FirearmResult(
    string Name,
    string? Url,
    string? ImageUrl,
    string Summary,
    string? Details,
    string? DetailSourceUrl,
    IReadOnlyList<FirearmAppearance> Appearances);
