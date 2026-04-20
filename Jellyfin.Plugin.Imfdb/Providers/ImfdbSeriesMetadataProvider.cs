using Jellyfin.Plugin.Imfdb.Services;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Imfdb.Providers;

/// <summary>
/// Adds IMFDB metadata to series during metadata refresh.
/// </summary>
public class ImfdbSeriesMetadataProvider : ICustomMetadataProvider<Series>, IHasOrder
{
    private readonly ImfdbMetadataEnricher _metadataEnricher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbSeriesMetadataProvider"/> class.
    /// </summary>
    /// <param name="metadataEnricher">Metadata enricher.</param>
    public ImfdbSeriesMetadataProvider(ImfdbMetadataEnricher metadataEnricher)
    {
        _metadataEnricher = metadataEnricher;
    }

    /// <inheritdoc />
    public string Name => "IMFDB";

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return _metadataEnricher.EnrichAsync(item, cancellationToken);
    }
}
