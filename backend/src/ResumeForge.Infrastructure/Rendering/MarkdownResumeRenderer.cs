using System.Text;
using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Rendering;

/// <summary>
/// Renders a <see cref="ResumeDocument"/> to clean, ATS-friendly plain markdown: a heading
/// per section, in <see cref="ResumeDocument.SectionOrder"/>, with no tables or exotic
/// formatting an applicant-tracking parser might choke on.
/// </summary>
public sealed class MarkdownResumeRenderer
{
    /// <summary>Renders <paramref name="doc"/> to markdown text.</summary>
    public string Render(ResumeDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var sb = new StringBuilder();
        AppendHeader(sb, doc.Basics);

        foreach (var section in doc.SectionOrder)
        {
            switch (section)
            {
                case SectionKind.Summary:
                    AppendSummary(sb, doc.Summary);
                    break;
                case SectionKind.Skills:
                    AppendSkills(sb, doc.Skills);
                    break;
                case SectionKind.Experience:
                    AppendExperience(sb, doc.Experience);
                    break;
                case SectionKind.Projects:
                    AppendProjects(sb, doc.Projects);
                    break;
                case SectionKind.Education:
                    AppendEducation(sb, doc.Education);
                    break;
                case SectionKind.Certifications:
                    AppendCertifications(sb, doc.Certifications);
                    break;
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendHeader(StringBuilder sb, ResumeBasics basics)
    {
        sb.Append("# ").Append(basics.FullName).Append('\n');

        if (!string.IsNullOrWhiteSpace(basics.Headline))
        {
            sb.Append(basics.Headline).Append('\n');
        }

        var contactParts = new List<string>();
        AddIfPresent(contactParts, basics.Email);
        AddIfPresent(contactParts, basics.Phone);
        AddIfPresent(contactParts, basics.Location);
        AddIfPresent(contactParts, basics.Website);
        AddIfPresent(contactParts, basics.LinkedIn);
        AddIfPresent(contactParts, basics.GitHub);

        if (contactParts.Count > 0)
        {
            sb.Append(string.Join(" | ", contactParts)).Append('\n');
        }

        sb.Append('\n');
    }

    private static void AppendSummary(StringBuilder sb, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        sb.Append("## Summary\n\n").Append(summary).Append("\n\n");
    }

    private static void AppendSkills(StringBuilder sb, IReadOnlyList<SkillGroup> groups)
    {
        var included = groups.Where(g => g.Included && g.Items.Count > 0).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("## Skills\n\n");

        foreach (var group in included)
        {
            var names = group.Items.Select(s => s.Emphasized ? $"**{s.Name}**" : s.Name);
            sb.Append("- **").Append(group.Label).Append(":** ").Append(string.Join(", ", names)).Append('\n');
        }

        sb.Append('\n');
    }

    private static void AppendExperience(StringBuilder sb, IReadOnlyList<ExperienceEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("## Experience\n\n");

        foreach (var entry in included)
        {
            sb.Append("### ").Append(entry.Role).Append(" — ").Append(entry.Organization).Append('\n');

            var meta = JoinMeta(entry.Location, DateRangeFormatter.Format(entry.StartDate, entry.EndDate));
            if (meta is not null)
            {
                sb.Append(meta).Append('\n');
            }

            AppendBullets(sb, entry.Bullets.Select(b => b.Text));
            sb.Append('\n');
        }
    }

    private static void AppendProjects(StringBuilder sb, IReadOnlyList<ProjectEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("## Projects\n\n");

        foreach (var entry in included)
        {
            sb.Append("### ").Append(entry.Name).Append('\n');

            if (!string.IsNullOrWhiteSpace(entry.Tagline))
            {
                sb.Append(entry.Tagline).Append('\n');
            }

            var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
            var links = JoinMeta(entry.Url, entry.RepoUrl);
            var meta = JoinMeta(range, links);
            if (meta is not null)
            {
                sb.Append(meta).Append('\n');
            }

            AppendBullets(sb, entry.Bullets.Select(b => b.Text));
            sb.Append('\n');
        }
    }

    private static void AppendEducation(StringBuilder sb, IReadOnlyList<EducationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("## Education\n\n");

        foreach (var entry in included)
        {
            sb.Append("### ").Append(entry.Credential).Append(" — ").Append(entry.Institution).Append('\n');

            var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
            var gpaText = entry.Gpa is { } gpaValue ? $"GPA: {gpaValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" : null;
            var meta = JoinMeta(entry.Location, JoinMeta(range, gpaText));
            if (meta is not null)
            {
                sb.Append(meta).Append('\n');
            }

            AppendBullets(sb, entry.Highlights);
            sb.Append('\n');
        }
    }

    private static void AppendCertifications(StringBuilder sb, IReadOnlyList<CertificationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("## Certifications\n\n");

        foreach (var entry in included)
        {
            sb.Append("- ").Append(entry.Name);

            var issuer = entry.Issuer;
            var issued = entry.IssuedOn is { } issuedOn ? issuedOn.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : null;
            var meta = JoinMeta(issuer, issued);
            if (meta is not null)
            {
                sb.Append(" — ").Append(meta);
            }

            sb.Append('\n');
        }

        sb.Append('\n');
    }

    private static void AppendBullets(StringBuilder sb, IEnumerable<string> bullets)
    {
        foreach (var text in bullets)
        {
            sb.Append("- ").Append(text).Append('\n');
        }
    }

    private static string? FormatOptionalRange(DateOnly? start, DateOnly? end)
    {
        if (start is { } s)
        {
            return DateRangeFormatter.Format(s, end);
        }

        return end is { } e ? e.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static void AddIfPresent(List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value);
        }
    }

    private static string? JoinMeta(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? null : right;
        }

        return string.IsNullOrWhiteSpace(right) ? left : $"{left} | {right}";
    }
}
