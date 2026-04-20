using Jellyfin.Plugin.Imfdb.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Imfdb.Providers;

/// <summary>
/// Adds IMFDB metadata to movies during metadata refresh.
/// </summary>
public class ImfdbMovieMetadataProvider : ICustomMetadataProvider<Movie>, IHasOrder
{
    private readonly ImfdbMetadataEnricher _metadataEnricher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbMovieMetadataProvider"/> class.
    /// </summary>
    /// <param name="metadataEnricher">Metadata enricher.</param>
    public ImfdbMovieMetadataProvider(ImfdbMetadataEnricher metadataEnricher)
    {
        _metadataEnricher = metadataEnricher;
    }

    /// <inheritdoc />
    public string Name => "IMFDB";

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return _metadataEnricher.EnrichAsync(item, cancellationToken);
    }
}
