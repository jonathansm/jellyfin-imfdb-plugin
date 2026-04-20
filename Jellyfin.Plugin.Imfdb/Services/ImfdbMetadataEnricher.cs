using System.Text;
using Jellyfin.Plugin.Imfdb.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// Writes IMFDB lookup results into native Jellyfin metadata fields.
/// </summary>
public class ImfdbMetadataEnricher
{
    private const string StartMarker = "[[IMFDB-FIREARMS-START]]";
    private const string EndMarker = "[[IMFDB-FIREARMS-END]]";
    private const string TagPrefix = "imfdb:";

    private readonly IImfdbClient _imfdbClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbMetadataEnricher"/> class.
    /// </summary>
    /// <param name="imfdbClient">IMFDB client.</param>
    public ImfdbMetadataEnricher(IImfdbClient imfdbClient)
    {
        _imfdbClient = imfdbClient;
    }

    /// <summary>
    /// Enriches an item with IMFDB metadata.
    /// </summary>
    /// <param name="item">The item to enrich.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata update type.</returns>
    public async Task<ItemUpdateType> EnrichAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null ||
            !configuration.EnableLookups ||
            (!configuration.AddFirearmsToOverview && !configuration.AddFirearmTags))
        {
            return ItemUpdateType.None;
        }

        item.ProviderIds.TryGetValue("Imdb", out var imdbId);

        var (sourceTitle, sourceUrl, firearms) = await _imfdbClient
            .LookupAsync(item.Name, item.ProductionYear, imdbId, cancellationToken)
            .ConfigureAwait(false);

        if (firearms.Count == 0)
        {
            return RemoveExistingMetadata(item) ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
        }

        var maxFirearms = Math.Clamp(configuration.MaxFirearms, 1, 100);
        var selectedFirearms = firearms
            .Take(maxFirearms)
            .ToArray();

        var changed = false;
        if (configuration.AddFirearmsToOverview)
        {
            changed |= SetOverviewBlock(item, sourceTitle, sourceUrl, selectedFirearms, firearms.Count);
        }
        else
        {
            changed |= RemoveOverviewBlock(item);
        }

        if (configuration.AddFirearmTags)
        {
            changed |= SetFirearmTags(item, selectedFirearms);
        }
        else
        {
            changed |= RemoveFirearmTags(item);
        }

        return changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }

    private bool RemoveExistingMetadata(BaseItem item)
    {
        return RemoveOverviewBlock(item) | RemoveFirearmTags(item);
    }

    private static bool SetOverviewBlock(
        BaseItem item,
        string? sourceTitle,
        string? sourceUrl,
        IReadOnlyList<FirearmResult> firearms,
        int totalFirearmCount)
    {
        var overviewWithoutBlock = StripOverviewBlock(item.Overview);
        var newBlock = BuildOverviewBlock(sourceTitle, sourceUrl, firearms, totalFirearmCount);
        var newOverview = string.IsNullOrWhiteSpace(overviewWithoutBlock)
            ? newBlock
            : overviewWithoutBlock.TrimEnd() + Environment.NewLine + Environment.NewLine + newBlock;

        if (string.Equals(item.Overview, newOverview, StringComparison.Ordinal))
        {
            return false;
        }

        item.Overview = newOverview;
        return true;
    }

    private static bool RemoveOverviewBlock(BaseItem item)
    {
        var newOverview = StripOverviewBlock(item.Overview);
        if (string.Equals(item.Overview, newOverview, StringComparison.Ordinal))
        {
            return false;
        }

        item.Overview = newOverview;
        return true;
    }

    private static bool SetFirearmTags(BaseItem item, IReadOnlyList<FirearmResult> firearms)
    {
        var existingTags = item.Tags
            .Where(static tag => !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        existingTags.AddRange(firearms.Select(static firearm => TagPrefix + firearm.Name));

        var newTags = existingTags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (item.Tags.SequenceEqual(newTags, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        item.Tags = newTags;
        return true;
    }

    private static bool RemoveFirearmTags(BaseItem item)
    {
        var newTags = item.Tags
            .Where(static tag => !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (item.Tags.SequenceEqual(newTags, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        item.Tags = newTags;
        return true;
    }

    private static string BuildOverviewBlock(
        string? sourceTitle,
        string? sourceUrl,
        IReadOnlyList<FirearmResult> firearms,
        int totalFirearmCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine(StartMarker);
        builder.AppendLine("IMFDB Firearms");
        if (!string.IsNullOrWhiteSpace(sourceTitle))
        {
            builder.Append("Source: ").Append(sourceTitle);
            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                builder.Append(" - ").Append(sourceUrl);
            }

            builder.AppendLine();
        }

        foreach (var firearm in firearms)
        {
            builder.Append("- ").Append(firearm.Name);
            var summary = BuildPlainSummary(firearm);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                builder.Append(": ").Append(summary);
            }

            builder.AppendLine();
        }

        if (totalFirearmCount > firearms.Count)
        {
            builder.AppendLine($"- {totalFirearmCount - firearms.Count} more firearm entries on IMFDB.");
        }

        builder.Append(EndMarker);
        return builder.ToString();
    }

    private static string BuildPlainSummary(FirearmResult firearm)
    {
        var appearances = firearm.Appearances
            .Select(static appearance =>
            {
                if (!string.IsNullOrWhiteSpace(appearance.Actor) && !string.IsNullOrWhiteSpace(appearance.Character))
                {
                    return appearance.Actor + " as " + appearance.Character;
                }

                return appearance.Actor ?? appearance.Character;
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return appearances.Length == 0 ? firearm.Summary : "Used by " + string.Join(", ", appearances) + ".";
    }

    private static string StripOverviewBlock(string? overview)
    {
        if (string.IsNullOrWhiteSpace(overview))
        {
            return string.Empty;
        }

        var start = overview.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = overview.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return overview.Trim();
        }

        end += EndMarker.Length;
        var stripped = overview.Remove(start, end - start);
        return stripped.Trim();
    }
}
