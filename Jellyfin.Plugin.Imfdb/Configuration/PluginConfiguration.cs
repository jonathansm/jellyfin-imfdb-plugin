using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Imfdb.Configuration;

/// <summary>
/// Stores plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether client lookups should be enabled.
    /// </summary>
    public bool EnableLookups { get; set; } = true;
}

