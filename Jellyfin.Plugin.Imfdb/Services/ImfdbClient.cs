using System.Globalization;
using System.Net;
using System.Security.Cryptography;
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
    private static readonly Uri WikiBaseUri = new("https://www.imfdb.org/wiki/");
    private static readonly Uri WikiApiUri = new("https://www.imfdb.org/api.php");
    private static readonly Uri WikiImageBaseUri = new("https://www.imfdb.org/images/");
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, null, Array.Empty<FirearmResult>());
        }

        try
        {
            var wikiMatches = await FindWikiMatchesAsync(title, year, cancellationToken).ConfigureAwait(false);
            foreach (var wikiMatch in wikiMatches)
            {
                var firearms = await ReadWikiPageAsync(wikiMatch.Title, wikiMatch.Url, cancellationToken).ConfigureAwait(false);
                if (firearms.Count > 0)
                {
                    return (wikiMatch.Title, wikiMatch.Url.ToString(), firearms);
                }
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

    private static async Task<IReadOnlyList<WikiPageMatch>> FindWikiMatchesAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var normalizedTitle = NormalizeTitle(title);
        var searches = year.HasValue
            ? new[] { title, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{title} {year.Value}") }
            : new[] { title };
        var matches = new Dictionary<string, WikiPageMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var search in searches)
        {
            var uri = new Uri(WikiApiUri + $"?action=query&list=search&format=json&srlimit=10&srsearch={Uri.EscapeDataString(search)}");
            using var document = JsonDocument.Parse(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("search", out var searchResults) ||
                searchResults.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var rank = 0;
            foreach (var result in searchResults.EnumerateArray())
            {
                var pageTitle = result.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(pageTitle))
                {
                    continue;
                }

                var snippet = result.TryGetProperty("snippet", out var snippetElement)
                    ? CleanText(snippetElement.GetString() ?? string.Empty)
                    : string.Empty;
                var score = ScoreTitle(pageTitle, normalizedTitle, year, snippet) - rank;
                rank++;
                if (score <= 0)
                {
                    continue;
                }

                if (!matches.TryGetValue(pageTitle, out var existing) || score > existing.Score)
                {
                    matches[pageTitle] = new WikiPageMatch(pageTitle, GetWikiPageUrl(pageTitle), score);
                }
            }
        }

        return matches.Values
            .OrderByDescending(static match => match.Score)
            .Take(6)
            .ToArray();
    }

    private async Task<IReadOnlyList<FirearmResult>> ReadWikiPageAsync(string pageTitle, Uri pageUrl, CancellationToken cancellationToken)
    {
        var sections = await ReadWikiSectionsAsync(pageTitle, pageUrl, cancellationToken).ConfigureAwait(false);
        return sections
            .Select(section => new FirearmResult(
                section.Name,
                section.SourceUrl,
                section.SourceUrl,
                section.ImageUrl,
                section.Caption ?? Truncate(section.Details ?? "Firearm listed on IMFDB.", 180),
                section.Details))
            .DistinctBy(static item => NormalizeTitle(item.Name))
            .ToArray();
    }

    private static async Task<IReadOnlyList<WikiFirearmSection>> ReadWikiSectionsAsync(string pageTitle, Uri pageUrl, CancellationToken cancellationToken)
    {
        var uri = new Uri(WikiApiUri + $"?action=parse&prop=wikitext&format=json&redirects=true&page={Uri.EscapeDataString(pageTitle)}");
        using var document = JsonDocument.Parse(await GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("parse", out var parse) ||
            !parse.TryGetProperty("wikitext", out var wikitextElement) ||
            !wikitextElement.TryGetProperty("*", out var textElement))
        {
            return Array.Empty<WikiFirearmSection>();
        }

        var wikiText = textElement.GetString() ?? string.Empty;
        var headings = WikiHeadingRegex().Matches(wikiText);
        var sections = new List<WikiFirearmSection>();
        foreach (Match heading in headings)
        {
            var name = CleanText(heading.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name) || IsNonFirearmSection(name))
            {
                continue;
            }

            var sectionText = ExtractSectionText(wikiText, heading);
            var details = ExtractSectionDetails(sectionText);
            var image = ExtractFirstImage(sectionText);
            if (details is null && image is null)
            {
                continue;
            }

            var sourceUrl = pageUrl + "#" + Uri.EscapeDataString(name.Replace(' ', '_'));
            sections.Add(new WikiFirearmSection(name, details, image?.ImageUrl, image?.Caption, sourceUrl));
        }

        return sections;
    }

    private static Uri GetWikiPageUrl(string pageTitle)
    {
        return new Uri(WikiBaseUri, Uri.EscapeDataString(pageTitle.Replace(' ', '_')));
    }

    private static string ExtractSectionText(string wikitext, Match heading)
    {
        var start = heading.Index + heading.Length;
        var next = WikiHeadingRegex().Match(wikitext, start);
        var length = next.Success ? next.Index - start : wikitext.Length - start;
        return wikitext.Substring(start, length);
    }

    private static string? ExtractSectionDetails(string section)
    {
        var image = WikiFileRegex().Match(section);
        if (image.Success)
        {
            section = section[..image.Index];
        }

        section = WikiMarkupRegex().Replace(section, ReplaceWikiMarkup);
        section = CleanText(section);
        return string.IsNullOrWhiteSpace(section) ? null : Truncate(section, 700);
    }

    private static string ReplaceWikiMarkup(Match match)
    {
        if (match.Groups["linkText"].Success)
        {
            return match.Groups["linkText"].Value;
        }

        if (match.Groups["externalText"].Success)
        {
            return match.Groups["externalText"].Value;
        }

        return " ";
    }

    private static WikiImage? ExtractFirstImage(string section)
    {
        var markup = ExtractFirstFileMarkup(section);
        if (string.IsNullOrWhiteSpace(markup))
        {
            return null;
        }

        var fields = SplitWikiFileMarkup(markup);
        if (fields.Count == 0)
        {
            return null;
        }

        var fileName = NormalizeWikiFileName(WikiFilePrefixRegex().Replace(fields[0], string.Empty));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var hash = Convert.ToHexString(MD5.HashData(System.Text.Encoding.UTF8.GetBytes(fileName))).ToLowerInvariant();
        var imageUrl = new Uri(WikiImageBaseUri, $"{hash[0]}/{hash[..2]}/{Uri.EscapeDataString(fileName)}").ToString();
        var caption = fields.Count > 1 ? CleanWikiText(fields[^1]) : null;
        return new WikiImage(imageUrl, string.IsNullOrWhiteSpace(caption) ? null : caption);
    }

    private static string NormalizeWikiFileName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var isFirstCharacter = true;
        foreach (var character in value.Trim())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate)
            {
                continue;
            }

            var normalizedCharacter = character == ' ' ? '_' : character;
            builder.Append(isFirstCharacter ? char.ToUpperInvariant(normalizedCharacter) : normalizedCharacter);
            isFirstCharacter = false;
        }

        return builder.ToString();
    }

    private static string? ExtractFirstFileMarkup(string section)
    {
        var image = WikiFileRegex().Match(section);
        if (!image.Success)
        {
            return null;
        }

        var depth = 0;
        for (var i = image.Index; i < section.Length - 1; i++)
        {
            var token = section.AsSpan(i, 2);
            if (token.SequenceEqual("[["))
            {
                depth++;
                i++;
                continue;
            }

            if (token.SequenceEqual("]]"))
            {
                depth--;
                i++;
                if (depth == 0)
                {
                    return section[image.Index..(i + 1)];
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SplitWikiFileMarkup(string markup)
    {
        var content = markup[2..^2];
        var fields = new List<string>();
        var start = 0;
        var linkDepth = 0;
        var templateDepth = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (i < content.Length - 1 && content.AsSpan(i, 2).SequenceEqual("[["))
            {
                linkDepth++;
                i++;
                continue;
            }

            if (i < content.Length - 1 && content.AsSpan(i, 2).SequenceEqual("]]"))
            {
                linkDepth = Math.Max(0, linkDepth - 1);
                i++;
                continue;
            }

            if (i < content.Length - 1 && content.AsSpan(i, 2).SequenceEqual("{{"))
            {
                templateDepth++;
                i++;
                continue;
            }

            if (i < content.Length - 1 && content.AsSpan(i, 2).SequenceEqual("}}"))
            {
                templateDepth = Math.Max(0, templateDepth - 1);
                i++;
                continue;
            }

            if (content[i] == '|' && linkDepth == 0 && templateDepth == 0)
            {
                fields.Add(content[start..i].Trim());
                start = i + 1;
            }
        }

        fields.Add(content[start..].Trim());
        return fields;
    }

    private static string? CleanWikiText(string value)
    {
        value = WikiMarkupRegex().Replace(value, ReplaceWikiMarkup);
        value = CleanText(value);
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    [GeneratedRegex("^==+\\s*(?<name>[^=]+?)\\s*==+\\s*$", RegexOptions.Multiline)]
    private static partial Regex WikiHeadingRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\[\\[(?:[^\\]|]+\\|)?(?<linkText>[^\\]]+)\\]\\]|\\{\\{[^}]+\\}\\}|\\[https?://[^\\s]+\\s*(?<externalText>[^\\]]*)\\]|'{2,}|={2,}|\\[\\[Image:[^\\]]+\\]\\]", RegexOptions.IgnoreCase)]
    private static partial Regex WikiMarkupRegex();

    [GeneratedRegex("\\[\\[(?:File|Image):(?<file>[^\\]|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WikiFileRegex();

    [GeneratedRegex("^(?:File|Image):", RegexOptions.IgnoreCase)]
    private static partial Regex WikiFilePrefixRegex();

    private sealed record WikiPageMatch(string Title, Uri Url, int Score);

    private sealed record WikiFirearmSection(string Name, string? Details, string? ImageUrl, string? Caption, string? SourceUrl);

    private sealed record WikiImage(string ImageUrl, string? Caption);
}
