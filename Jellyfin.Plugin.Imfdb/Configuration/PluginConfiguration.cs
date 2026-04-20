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
    /// Gets or sets a value indicating whether the Jellyfin Web firearm row should be injected by File Transformation.
    /// </summary>
    public bool EnableWebUiInjection { get; set; } = true;
}
