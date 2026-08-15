using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResumeForge.Infrastructure.Jobs;

/// <summary>
/// Pure, network-free HTML-to-text extraction for job posting pages, using regex/span
/// parsing rather than a full HTML parser (none is available in this project). This is a
/// deliberately focused extractor, not a general-purpose one:
/// <list type="bullet">
/// <item>It strips tags rather than building a DOM, so it cannot distinguish visible text
/// from, say, an <c>alt</c> attribute's contents or an inline SVG's text nodes — anything
/// between angle brackets is discarded, and anything that is not a tag is kept.</item>
/// <item>It removes <c>&lt;script&gt;</c>, <c>&lt;style&gt;</c>, <c>&lt;nav&gt;</c>, and
/// <c>&lt;footer&gt;</c> blocks by a non-nesting regex match, so a malformed page with one
/// of those tags nested inside itself will not be fully stripped.</item>
/// <item>All structural whitespace (paragraph breaks, line breaks) collapses to single
/// spaces; no attempt is made to reconstruct paragraph boundaries.</item>
/// </list>
/// </summary>
public static partial class HtmlTextExtractor
{
    /// <summary>
    /// Extracts readable page text: strips script/style/nav/footer blocks and HTML
    /// comments, strips remaining tags, decodes entities, and collapses whitespace.
    /// </summary>
    public static string ExtractReadableText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var withoutNoise = NoiseBlockRegex().Replace(html, " ");
        var withoutComments = CommentRegex().Replace(withoutNoise, " ");
        var withoutTags = TagRegex().Replace(withoutComments, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    /// <summary>
    /// Extracts <c>Company</c>/<c>Title</c>/<c>Location</c> from a schema.org
    /// <c>JobPosting</c> JSON-LD block when present, falling back to OpenGraph meta tags
    /// for title and company.
    /// </summary>
    public static JobPostingMetadata ExtractMetadata(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var jsonLd = TryExtractFromJsonLd(html);
        var openGraph = ExtractOpenGraph(html);

        return new JobPostingMetadata
        {
            Title = jsonLd.Title ?? openGraph.Title,
            Company = jsonLd.Company ?? openGraph.SiteName,
            Location = jsonLd.Location,
        };
    }

    private static (string? Title, string? Company, string? Location) TryExtractFromJsonLd(string html)
    {
        foreach (Match match in JsonLdScriptRegex().Matches(html))
        {
            var raw = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var candidates = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : Enumerable.Repeat(document.RootElement, 1);

                foreach (var candidate in candidates)
                {
                    if (TryReadJobPosting(candidate, out var result))
                    {
                        return result;
                    }
                }
            }
        }

        return (null, null, null);
    }

    private static bool TryReadJobPosting(JsonElement element, out (string? Title, string? Company, string? Location) result)
    {
        result = (null, null, null);

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("@type", out var typeElement))
        {
            return false;
        }

        var isJobPosting = typeElement.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeElement.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => typeElement.EnumerateArray()
                .Any(e => e.ValueKind == JsonValueKind.String && string.Equals(e.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };

        if (!isJobPosting)
        {
            return false;
        }

        var title = element.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
            ? titleEl.GetString()
            : null;

        string? company = null;
        if (element.TryGetProperty("hiringOrganization", out var orgEl))
        {
            company = orgEl.ValueKind switch
            {
                JsonValueKind.Object when orgEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String => nameEl.GetString(),
                JsonValueKind.String => orgEl.GetString(),
                _ => null,
            };
        }

        string? location = null;
        if (element.TryGetProperty("jobLocation", out var locationEl))
        {
            location = ExtractLocation(locationEl);
        }

        result = (title, company, location);
        return true;
    }

    private static string? ExtractLocation(JsonElement jobLocationElement)
    {
        var place = jobLocationElement.ValueKind == JsonValueKind.Array
            ? (jobLocationElement.GetArrayLength() > 0 ? jobLocationElement[0] : default)
            : jobLocationElement;

        if (place.ValueKind != JsonValueKind.Object || !place.TryGetProperty("address", out var addressEl))
        {
            return null;
        }

        if (addressEl.ValueKind == JsonValueKind.String)
        {
            return addressEl.GetString();
        }

        if (addressEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var locality = addressEl.TryGetProperty("addressLocality", out var localityEl) && localityEl.ValueKind == JsonValueKind.String
            ? localityEl.GetString()
            : null;
        var region = addressEl.TryGetProperty("addressRegion", out var regionEl) && regionEl.ValueKind == JsonValueKind.String
            ? regionEl.GetString()
            : null;

        return (locality, region) switch
        {
            ({ Length: > 0 } l, { Length: > 0 } r) => $"{l}, {r}",
            ({ Length: > 0 } l, _) => l,
            (_, { Length: > 0 } r) => r,
            _ => null,
        };
    }

    private static (string? Title, string? SiteName) ExtractOpenGraph(string html)
    {
        string? title = null;
        string? siteName = null;

        foreach (Match tagMatch in MetaTagRegex().Matches(html))
        {
            var tag = tagMatch.Value;
            var property = ExtractAttribute(tag, "property") ?? ExtractAttribute(tag, "name");
            var content = ExtractAttribute(tag, "content");

            if (property is null || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (title is null && string.Equals(property, "og:title", StringComparison.OrdinalIgnoreCase))
            {
                title = WebUtility.HtmlDecode(content);
            }
            else if (siteName is null && string.Equals(property, "og:site_name", StringComparison.OrdinalIgnoreCase))
            {
                siteName = WebUtility.HtmlDecode(content);
            }
        }

        return (title, siteName);
    }

    private static string? ExtractAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(tag, $"""{attributeName}\s*=\s*("([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    [GeneratedRegex(@"<(script|style|nav|footer)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NoiseBlockRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<meta\s+[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(
        """<script[^>]*type\s*=\s*("application/ld\+json"|'application/ld\+json')[^>]*>(.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdScriptRegex();
}
