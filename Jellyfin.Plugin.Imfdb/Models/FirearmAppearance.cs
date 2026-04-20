namespace Jellyfin.Plugin.Imfdb.Models;

/// <summary>
/// Describes a single firearm appearance in a title.
/// </summary>
/// <param name="Actor">Actor name, when known.</param>
/// <param name="Character">Character name, when known.</param>
/// <param name="Notes">Appearance notes, when known.</param>
public sealed record FirearmAppearance(string? Actor, string? Character, string? Notes);

