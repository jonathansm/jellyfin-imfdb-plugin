using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Imfdb.Web;

/// <summary>
/// Payload passed from File Transformation into plugin callbacks.
/// </summary>
public sealed class FileTransformationPayload
{
    /// <summary>
    /// Gets or sets the file contents.
    /// </summary>
    [JsonPropertyName("contents")]
    public string Contents { get; set; } = string.Empty;
}

