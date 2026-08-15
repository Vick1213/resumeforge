using System.Globalization;
using System.Text;
using ResumeForge.Domain.Knowledge;

namespace ResumeForge.Infrastructure.Knowledge;

/// <summary>
/// Shared constants and formatting helpers used by both
/// <see cref="MarkdownKnowledgeBaseReader"/> and <see cref="MarkdownKnowledgeBaseWriter"/>
/// so the two stay in lockstep on frontmatter field order and value formatting — the
/// foundation of round-trip stability (CONTRACTS.md §3).
/// </summary>
internal static class KnowledgeMarkdownFormat
{
    /// <summary>Frontmatter field order for <c>profile/experience/*.md</c>, after the leading <c>type</c> key.</summary>
    public static readonly IReadOnlyList<string> ExperienceFieldOrder =
        ["role", "organization", "location", "startDate", "endDate", "tech", "tags"];

    /// <summary>Frontmatter field order for <c>profile/projects/*.md</c>, after the leading <c>type</c> key.</summary>
    public static readonly IReadOnlyList<string> ProjectFieldOrder =
        ["name", "tagline", "url", "repoUrl", "startDate", "endDate", "tech", "tags", "source", "stars"];

    /// <summary>Frontmatter field order for <c>profile/education/*.md</c>, after the leading <c>type</c> key.</summary>
    public static readonly IReadOnlyList<string> EducationFieldOrder =
        ["institution", "credential", "location", "startDate", "endDate", "gpa"];

    /// <summary>Frontmatter field order for <c>profile/certifications/*.md</c>, after the leading <c>type</c> key.</summary>
    public static readonly IReadOnlyList<string> CertificationFieldOrder =
        ["name", "issuer", "issuedOn", "credentialUrl"];

    /// <summary>Frontmatter field order for <c>profile/basics.md</c>, which carries no <c>type</c> key.</summary>
    public static readonly IReadOnlyList<string> BasicsFieldOrder =
        ["fullName", "headline", "email", "phone", "location", "website", "linkedin", "github"];

    /// <summary>The literal <c>type:</c> value written for each category.</summary>
    public static string TypeLiteral(KnowledgeItemType type) => type switch
    {
        KnowledgeItemType.Experience => "experience",
        KnowledgeItemType.Project => "project",
        KnowledgeItemType.Education => "education",
        KnowledgeItemType.Certification => "certification",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Basics has no 'type' frontmatter key."),
    };

    /// <summary>Maps a <c>type:</c> literal (case-insensitive) to a <see cref="KnowledgeItemType"/>.</summary>
    public static bool TryParseTypeLiteral(string? literal, out KnowledgeItemType type)
    {
        switch (literal?.Trim().ToLowerInvariant())
        {
            case "experience":
                type = KnowledgeItemType.Experience;
                return true;
            case "project":
                type = KnowledgeItemType.Project;
                return true;
            case "education":
                type = KnowledgeItemType.Education;
                return true;
            case "certification":
                type = KnowledgeItemType.Certification;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>
    /// Reconstructs the frontmatter text for a knowledge-base date. Ambiguity note: a
    /// <see cref="DateOnly"/> with day 1 is always written back as <c>yyyy-MM</c> (the
    /// precision every date in the bundled sample profile uses); a bare <c>yyyy</c> value
    /// therefore round-trips as <c>yyyy-MM</c> rather than its original form. Ongoing
    /// items always write back as the literal <c>present</c>, so an original
    /// <c>current</c> literal also normalizes on write. Returns null when there is
    /// nothing to write (no date, not current).
    /// </summary>
    public static string? FormatDate(DateOnly? date, bool isCurrent)
    {
        if (isCurrent)
        {
            return "present";
        }

        if (date is not { } value)
        {
            return null;
        }

        return value.Day == 1
            ? value.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a list of raw display tokens as an inline YAML flow sequence, e.g. <c>[C#, .NET]</c>.</summary>
    public static string FormatInlineList(IReadOnlyList<string> items) =>
        $"[{string.Join(", ", items)}]";

    /// <summary>Formats a single frontmatter scalar value, quoting it only when required by YAML's plain-scalar rules.</summary>
    public static string FormatScalar(string value)
    {
        return NeedsQuoting(value) ? QuoteScalar(value) : value;
    }

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return true;
        }

        if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
        {
            return true;
        }

        var first = value[0];
        if (first is '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|'
            or '>' or '\'' or '"' or '%' or '@' or '`')
        {
            // A leading '-' is only ambiguous when followed by a space (block-sequence marker);
            // other indicator characters are always ambiguous as the first character of a plain scalar.
            if (first != '-' || value.Length == 1 || value[1] == ' ')
            {
                return true;
            }
        }

        if (value.Contains(": ", StringComparison.Ordinal) || value.EndsWith(':') || value.Contains(" #", StringComparison.Ordinal))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "false" or "null" or "~" or "yes" or "no" or "on" or "off" => true,
            _ => false,
        };
    }

    private static string QuoteScalar(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
