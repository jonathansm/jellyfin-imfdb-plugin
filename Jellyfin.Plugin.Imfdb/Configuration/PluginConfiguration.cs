using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Imfdb.Configuration;

/// <summary>
/// Stores plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether IMFDB lookups should be enabled.
    /// </summary>
    public bool EnableLookups { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether firearm results should be appended to item overviews.
    /// </summary>
    public bool AddFirearmsToOverview { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether firearm tags should be added to items.
    /// </summary>
    public bool AddFirearmTags { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of firearms to write into item metadata.
    /// </summary>
    public int MaxFirearms { get; set; } = 20;
}
