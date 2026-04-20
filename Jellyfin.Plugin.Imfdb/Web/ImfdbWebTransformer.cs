namespace Jellyfin.Plugin.Imfdb.Web;

/// <summary>
/// File Transformation callback methods for Jellyfin Web.
/// </summary>
public static class ImfdbWebTransformer
{
    private const string ScriptTag = "<script defer src=\"../Imfdb/ClientScript\"></script>";
    private const string Marker = "<!-- jellyfin-imfdb-client -->";

    /// <summary>
    /// Injects the IMFDB client script into Jellyfin Web's index page.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Transformed contents.</returns>
    public static string TransformIndex(FileTransformationPayload payload)
    {
        var contents = payload.Contents;
        if (Plugin.Instance?.Configuration.EnableWebUiInjection == false ||
            string.IsNullOrWhiteSpace(contents) ||
            contents.Contains(Marker, StringComparison.Ordinal))
        {
            return contents;
        }

        var injection = Marker + ScriptTag;
        var bodyIndex = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
        {
            return contents.Insert(bodyIndex, injection);
        }

        return contents + injection;
    }
}
