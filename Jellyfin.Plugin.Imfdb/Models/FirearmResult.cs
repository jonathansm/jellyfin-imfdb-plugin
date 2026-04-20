namespace Jellyfin.Plugin.Imfdb.Models;

/// <summary>
/// Firearm grouped for the client card row.
/// </summary>
/// <param name="Name">Firearm name.</param>
/// <param name="Url">IMFDB or IMFDB Browser firearm URL.</param>
/// <param name="Summary">Short summary for the card.</param>
/// <param name="Appearances">Known appearances in the title.</param>
public sealed record FirearmResult(
    string Name,
    string? Url,
    string Summary,
    IReadOnlyList<FirearmAppearance> Appearances);

