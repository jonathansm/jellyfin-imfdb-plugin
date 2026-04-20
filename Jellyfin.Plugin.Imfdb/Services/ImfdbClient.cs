using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Imfdb.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Services;

/// <summary>
/// IMFDB lookup client.
/// </summary>
public partial class ImfdbClient : IImfdbClient
{
    private static readonly Uri BrowserBaseUri = new("https://browser.imfdb.org/");
    private static readonly Uri WikiApiUri = new("https://www.imfdb.org/api.php");
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly ILogger<ImfdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImfdbClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ImfdbClient(ILogger<ImfdbClient> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(string? SourceTitle, string? SourceUrl, IReadOnlyList<FirearmResult> Firearms)> LookupAsync(
        string title,
        int? year,
        string? imdbId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, null, Array.Empty<FirearmResult>());
        }

        try
        {
            var browserMatch = await FindBrowserMatchAsync(title, year, cancellationToken).ConfigureAwait(false);
            if (browserMatch is not null)
            {
                var firearms = await ReadBrowserMediaAsync(browserMatch.Value.Url, cancellationToken).ConfigureAwait(false);
                if (firearms.Count > 0)
                {
                    return (browserMatch.Value.Title, browserMatch.Value.Url.ToString(), firearms);
                }
            }

            var wikiMatch = await FindWikiMatchAsync(title, year, imdbId, cancellationToken).ConfigureAwait(false);
            if (wikiMatch is not null)
            {
                var firearms = await ReadWikiPageAsync(wikiMatch.Value.Title, wikiMatch.Value.Url, cancellationToken).ConfigureAwait(false);
                return (wikiMatch.Value.Title, wikiMatch.Value.Url.ToString(), firearms);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IMFDB lookup failed for {Title}", title);
        }

        return (null, null, Array.Empty<FirearmResult>());
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.Imfdb/0.1");
        return client;
    }

    private async Task<(string Title, Uri Url)?> FindBrowserMatchAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            $"media?q={Uri.EscapeDataString(title)}",
            $"media?search={Uri.EscapeDataString(title)}",
            $"media?query={Uri.EscapeDataString(title)}"
        };

        foreach (var path in candidates)
        {
            var uri = new Uri(BrowserBaseUri, path);
            var html = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            var match = SelectBestMediaLink(html, title, year);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static (string Title, Uri Url)? SelectBestMediaLink(string html, string title, int? year)
    {
        var normalizedTitle = NormalizeTitle(title);
        var matches = BrowserMediaLinkRegex().Matches(html);
        (string Title, Uri Url, int Score)? best = null;

        foreach (Match match in matches)
        {
            var relativeUrl = WebUtility.HtmlDecode(match.Groups["url"].Value);
            var linkText = CleanText(match.Groups["title"].Value);
            var rowTail = CleanText(match.Groups["tail"].Value);
            var score = ScoreTitle(linkText, normalizedTitle, year, rowTail);
            if (score <= 0)
            {
                continue;
            }

            if (best is null || score > best.Value.Score)
            {
                best = (linkText, new Uri(BrowserBaseUri, relativeUrl.TrimStart('/')), score);
            }
        }

        return best is null ? null : (best.Value.Title, best.Value.Url);
    }

    private async Task<IReadOnlyList<FirearmResult>> ReadBrowserMediaAsync(Uri mediaUrl, CancellationToken cancellationToken)
    {
        var html = await GetStringAsync(mediaUrl, cancellationToken).ConfigureAwait(false);
        var rows = BrowserAppearanceRowRegex().Matches(html);
        var grouped = new Dictionary<string, List<(string FirearmUrl, FirearmAppearance Appearance)>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match row in rows)
        {
            var actor = CleanText(row.Groups["actor"].Value);
            var character = CleanText(row.Groups["character"].Value);
            var firearmName = CleanText(row.Groups["firearm"].Value);
            var firearmUrl = WebUtility.HtmlDecode(row.Groups["url"].Value);
            var notes = CleanText(row.Groups["notes"].Value);

            if (string.IsNullOrWhiteSpace(firearmName))
            {
                continue;
            }

            var key = NormalizeTitle(firearmName);
            names[key] = firearmName;
            if (!grouped.TryGetValue(key, out var appearances))
            {
                appearances = new List<(string FirearmUrl, FirearmAppearance Appearance)>();
                grouped[key] = appearances;
            }

            appearances.Add((firearmUrl, new FirearmAppearance(
                NullIfEmpty(actor),
                NullIfEmpty(character),
                NullIfDash(notes))));
        }

        return grouped
            .OrderByDescending(static pair => pair.Value.Count)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var url = pair.Value.Select(static value => value.FirearmUrl).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
                var appearances = pair.Value.Select(static value => value.Appearance).ToArray();
                return new FirearmResult(
                    names[pair.Key],
                    string.IsNullOrWhiteSpace(url) ? null : new Uri(BrowserBaseUri, url.TrimStart('/')).ToString(),
                    BuildSummary(appearances),
                    appearances);
            })
            .ToArray();
    }

    private async Task<(string Title, Uri Url)?> FindWikiMatchAsync(string title, int? year, string? imdbId, CancellationToken cancellationToken)
    {
        var search = !string.IsNullOrWhiteSpace(imdbId)
            ? $"{title} {year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty} {imdbId}"
            : year.HasValue ? $"{title} {year.Value}" : title;
        var uri = new Uri(WikiApiUri + $"?action=query&list=search&srnamespace=0&srlimit=10&format=json&srsearch={Uri.EscapeDataString(search)}");
        using var document = JsonDocument.Parse(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("search", out var results))
        {
            return null;
        }

        (string Title, int Score)? best = null;
        foreach (var result in results.EnumerateArray())
        {
            var pageTitle = result.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(pageTitle))
            {
                continue;
            }

            var score = ScoreTitle(pageTitle, NormalizeTitle(title), year, pageTitle);
            if (best is null || score > best.Value.Score)
            {
                best = (pageTitle, score);
            }
        }

        if (best is null || best.Value.Score <= 0)
        {
            return null;
        }

        var pageUrl = new Uri("https://www.imfdb.org/wiki/" + Uri.EscapeDataString(best.Value.Title.Replace(' ', '_')));
        return (best.Value.Title, pageUrl);
    }

    private async Task<IReadOnlyList<FirearmResult>> ReadWikiPageAsync(string pageTitle, Uri pageUrl, CancellationToken cancellationToken)
    {
        var uri = new Uri(WikiApiUri + $"?action=parse&prop=wikitext&format=json&page={Uri.EscapeDataString(pageTitle)}");
        using var document = JsonDocument.Parse(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("parse", out var parse) ||
            !parse.TryGetProperty("wikitext", out var wikitext) ||
            !wikitext.TryGetProperty("*", out var textElement))
        {
            return Array.Empty<FirearmResult>();
        }

        var wikitext = textElement.GetString() ?? string.Empty;
        var headings = WikiHeadingRegex().Matches(wikitext);
        var results = new List<FirearmResult>();

        foreach (Match heading in headings)
        {
            var name = CleanText(heading.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name) || IsNonFirearmSection(name))
            {
                continue;
            }

            var summary = ExtractSectionSummary(wikitext, heading);
            results.Add(new FirearmResult(
                name,
                pageUrl + "#" + Uri.EscapeDataString(name.Replace(' ', '_')),
                summary,
                Array.Empty<FirearmAppearance>()));
        }

        return results
            .DistinctBy(static item => NormalizeTitle(item.Name))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ExtractSectionSummary(string wikitext, Match heading)
    {
        var start = heading.Index + heading.Length;
        var next = WikiHeadingRegex().Match(wikitext, start);
        var length = next.Success ? next.Index - start : wikitext.Length - start;
        var section = wikitext.Substring(start, Math.Min(length, 700));
        section = WikiMarkupRegex().Replace(section, " ");
        section = CleanText(section);
        return string.IsNullOrWhiteSpace(section) ? "Firearm listed on IMFDB." : Truncate(section, 180);
    }

    private static int ScoreTitle(string candidate, string normalizedTitle, int? year, string context)
    {
        var normalizedCandidate = NormalizeTitle(candidate);
        var score = 0;
        if (normalizedCandidate.Equals(normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (normalizedCandidate.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (year.HasValue && context.Contains(year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            score += 20;
        }

        return score;
    }

    private static string BuildSummary(IReadOnlyList<FirearmAppearance> appearances)
    {
        var actorNames = appearances
            .Select(static appearance => appearance.Actor)
            .Where(static actor => !string.IsNullOrWhiteSpace(actor))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (actorNames.Length == 0)
        {
            return $"{appearances.Count} appearance{(appearances.Count == 1 ? string.Empty : "s")} listed on IMFDB.";
        }

        return "Used by " + string.Join(", ", actorNames) + (appearances.Count > actorNames.Length ? $" and {appearances.Count - actorNames.Length} more." : ".");
    }

    private static string NormalizeTitle(string value)
    {
        value = WebUtility.HtmlDecode(value).ToLowerInvariant();
        value = NonWordRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(value, " ").Trim();
    }

    private static string CleanText(string value)
    {
        value = HtmlTagRegex().Replace(value, " ");
        value = WebUtility.HtmlDecode(value);
        value = WhitespaceRegex().Replace(value, " ");
        return value.Trim();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NullIfDash(string value) => string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + "...";
    }

    private static bool IsNonFirearmSection(string name)
    {
        var normalized = NormalizeTitle(name);
        return normalized is "contents" or "see also" or "external links" or "references" or "cast" or "film" or "television" or "weapons";
    }

    private static async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("<a\\s+href=\"(?<url>/media/\\d+)\"[^>]*>(?<title>.*?)</a>(?<tail>[^<]*(?:<[^a][^>]*>[^<]*){0,8})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BrowserMediaLinkRegex();

    [GeneratedRegex("<tr>\\s*<td[^>]*>\\s*<a[^>]*>(?<actor>.*?)</a>\\s*</td>\\s*<td[^>]*>(?<character>.*?)</td>\\s*<td[^>]*>\\s*<a\\s+href=\"(?<url>/firearms/\\d+)\"[^>]*>(?<firearm>.*?)</a>\\s*</td>\\s*<td[^>]*>(?<notes>.*?)</td>\\s*</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BrowserAppearanceRowRegex();

    [GeneratedRegex("^==+\\s*(?<name>[^=]+?)\\s*==+\\s*$", RegexOptions.Multiline)]
    private static partial Regex WikiHeadingRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\[\\[(?:[^\\]|]+\\|)?([^\\]]+)\\]\\]|\\{\\{[^}]+\\}\\}|\\[https?://[^\\s]+\\s*([^\\]]*)\\]|'{2,}|={2,}|\\[\\[Image:[^\\]]+\\]\\]", RegexOptions.IgnoreCase)]
    private static partial Regex WikiMarkupRegex();
}
