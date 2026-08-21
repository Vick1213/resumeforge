using System.Globalization;
using System.Net;
using System.Text;
using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Rendering;

/// <summary>
/// Renders a <see cref="ResumeDocument"/> to a single, self-contained HTML document with
/// embedded CSS: no external stylesheets, fonts, or scripts, so the file opens correctly
/// on its own and prints cleanly to A4 or US Letter.
/// </summary>
public sealed class HtmlResumeRenderer
{
    /// <summary>Renders <paramref name="doc"/> to a complete HTML document.</summary>
    public string Render(ResumeDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Enc(doc.Basics.FullName)).Append(" — Resume</title>\n");
        sb.Append("<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n<main class=\"resume\">\n");

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

        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ResumeBasics basics)
    {
        sb.Append("<header class=\"header\">\n<h1>").Append(Enc(basics.FullName)).Append("</h1>\n");

        if (!string.IsNullOrWhiteSpace(basics.Headline))
        {
            sb.Append("<p class=\"headline\">").Append(Enc(basics.Headline)).Append("</p>\n");
        }

        var items = new List<string>();
        AddContactItem(items, basics.Email, basics.Email is null ? null : $"mailto:{basics.Email}");
        AddContactItem(items, basics.Phone, null);
        AddContactItem(items, basics.Location, null);
        AddContactItem(items, DisplayUrl(basics.Website), basics.Website);
        AddContactItem(items, DisplayUrl(basics.LinkedIn), basics.LinkedIn);
        AddContactItem(items, DisplayUrl(basics.GitHub), basics.GitHub);

        if (items.Count > 0)
        {
            sb.Append("<p class=\"contact\">").Append(string.Join(" <span class=\"sep\">·</span> ", items)).Append("</p>\n");
        }

        sb.Append("</header>\n");
    }

    private static void AddContactItem(List<string> items, string? text, string? href)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        items.Add(href is null ? Enc(text) : $"<a href=\"{Enc(href)}\">{Enc(text)}</a>");
    }

    private static string? DisplayUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                   .TrimEnd('/');
    }

    private static void AppendSummary(StringBuilder sb, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Summary</h2>\n<p class=\"summary\">")
          .Append(Enc(summary).Replace("\n", "<br>", StringComparison.Ordinal))
          .Append("</p>\n</section>\n");
    }

    private static void AppendSkills(StringBuilder sb, IReadOnlyList<SkillGroup> groups)
    {
        var included = groups.Where(g => g.Included && g.Items.Count > 0).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Skills</h2>\n<div class=\"skills\">\n");

        foreach (var group in included)
        {
            // Label inline, leading the same line its skills run on — see
            // PdfResumeRenderer.ComposeSkills for why the fixed label column was dropped.
            sb.Append("<p class=\"skill-row\"><span class=\"skill-label\">").Append(Enc(group.Label)).Append(":</span> ");
            sb.Append(string.Join(", ", group.Items.Select(s => s.Emphasized ? $"<strong>{Enc(s.Name)}</strong>" : Enc(s.Name))));
            sb.Append("</p>\n");
        }

        sb.Append("</div>\n</section>\n");
    }

    private static void AppendExperience(StringBuilder sb, IReadOnlyList<ExperienceEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Experience</h2>\n");

        foreach (var entry in included)
        {
            sb.Append("<article class=\"entry\">\n<div class=\"entry-head\"><h3>")
              .Append(Enc(entry.Role)).Append(" <span class=\"org\">— ").Append(Enc(entry.Organization)).Append("</span>");

            if (!string.IsNullOrWhiteSpace(entry.Location))
            {
                sb.Append(" <span class=\"loc\">· ").Append(Enc(entry.Location)).Append("</span>");
            }

            sb.Append("</h3>");
            sb.Append("<span class=\"dates\">").Append(Enc(DateRangeFormatter.Format(entry.StartDate, entry.EndDate))).Append("</span>");
            sb.Append("</div>\n");

            AppendBullets(sb, entry.Bullets.Select(b => b.Text));
            sb.Append("</article>\n");
        }

        sb.Append("</section>\n");
    }

    private static void AppendProjects(StringBuilder sb, IReadOnlyList<ProjectEntry> entries)
    {
        var included = entries.Where(e => e.Included && e.HasRenderableContent()).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Projects</h2>\n");

        foreach (var entry in included)
        {
            sb.Append("<article class=\"entry\">\n<div class=\"entry-head\"><h3>").Append(Enc(entry.Name)).Append("</h3>");

            var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
            if (range is not null)
            {
                sb.Append("<span class=\"dates\">").Append(Enc(range)).Append("</span>");
            }

            sb.Append("</div>\n");

            if (entry.ShouldRenderTagline(out var tagline))
            {
                sb.Append("<p class=\"tagline\">").Append(Enc(tagline)).Append("</p>\n");
            }

            var links = new List<string>();
            AddContactItem(links, DisplayUrl(entry.Url), entry.Url);
            AddContactItem(links, DisplayUrl(entry.RepoUrl), entry.RepoUrl);
            if (links.Count > 0)
            {
                sb.Append("<p class=\"links\">").Append(string.Join(" <span class=\"sep\">·</span> ", links)).Append("</p>\n");
            }

            AppendBullets(sb, entry.Bullets.Select(b => b.Text));
            sb.Append("</article>\n");
        }

        sb.Append("</section>\n");
    }

    private static void AppendEducation(StringBuilder sb, IReadOnlyList<EducationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Education</h2>\n");

        foreach (var entry in included)
        {
            sb.Append("<article class=\"entry\">\n<div class=\"entry-head\"><h3>")
              .Append(Enc(entry.Credential)).Append(" <span class=\"org\">— ").Append(Enc(entry.Institution)).Append("</span></h3>");

            var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
            if (range is not null)
            {
                sb.Append("<span class=\"dates\">").Append(Enc(range)).Append("</span>");
            }

            sb.Append("</div>\n");

            // Location, GPA, and every highlight on one line — see
            // PdfResumeRenderer.EducationMetaLine for why coursework stopped being a bullet.
            var metaParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Location))
            {
                metaParts.Add(Enc(entry.Location));
            }

            if (entry.Gpa is { } gpa)
            {
                metaParts.Add($"GPA: {gpa.ToString("0.00", CultureInfo.InvariantCulture)}");
            }

            metaParts.AddRange(entry.Highlights.Where(h => !string.IsNullOrWhiteSpace(h)).Select(Enc));

            if (metaParts.Count > 0)
            {
                sb.Append("<p class=\"location\">").Append(string.Join(" <span class=\"sep\">·</span> ", metaParts)).Append("</p>\n");
            }
            sb.Append("</article>\n");
        }

        sb.Append("</section>\n");
    }

    private static void AppendCertifications(StringBuilder sb, IReadOnlyList<CertificationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        sb.Append("<section class=\"section\">\n<h2>Certifications</h2>\n<ul class=\"certs\">\n");

        foreach (var entry in included)
        {
            sb.Append("<li><strong>").Append(Enc(entry.Name)).Append("</strong>");

            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Issuer))
            {
                meta.Add(Enc(entry.Issuer));
            }

            if (entry.IssuedOn is { } issuedOn)
            {
                meta.Add(Enc(DateRangeFormatter.FormatMonth(issuedOn)));
            }

            if (meta.Count > 0)
            {
                sb.Append(" — ").Append(string.Join(", ", meta));
            }

            sb.Append("</li>\n");
        }

        sb.Append("</ul>\n</section>\n");
    }

    private static void AppendBullets(StringBuilder sb, IEnumerable<string> bullets)
    {
        var list = bullets.ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.Append("<ul class=\"bullets\">\n");
        foreach (var text in list)
        {
            sb.Append("<li>").Append(Enc(text)).Append("</li>\n");
        }

        sb.Append("</ul>\n");
    }

    private static string? FormatOptionalRange(DateOnly? start, DateOnly? end)
    {
        if (start is { } s)
        {
            return DateRangeFormatter.Format(s, end);
        }

        return end is { } e ? DateRangeFormatter.FormatMonth(e) : null;
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private const string Css = """
        :root {
          --ink: #1a1d23;
          --muted: #5b6270;
          --accent: #2a4d8f;
          --rule: #d8dce3;
          --page: #ffffff;
        }
        * { box-sizing: border-box; }
        html, body { padding: 0; margin: 0; background: var(--page); }
        body {
          font-family: -apple-system, "Segoe UI", Helvetica, Arial, sans-serif;
          color: var(--ink);
          line-height: 1.45;
          font-size: 14px;
        }
        .resume { max-width: 780px; margin: 0 auto; padding: 24px 32px 36px; }
        /* Compact header, mirroring PdfResumeRenderer: name at ~1.5x body, headline a step
           under body, contact two steps under — the header should cost a few lines, not a
           seventh of the page. */
        .header { margin-bottom: 4px; text-align: center; }
        .header h1 { margin: 0; font-size: 17px; letter-spacing: 0.2px; line-height: 1.25; }
        .headline { margin: 0; color: var(--accent); font-weight: 600; font-size: 10.5px; }
        .contact { margin: 0; color: var(--muted); font-size: 9px; }
        .contact a { color: var(--muted); text-decoration: none; }
        .contact a:hover { text-decoration: underline; }
        .sep { color: var(--rule); }
        .section { margin-top: 7px; }
        .section h2 {
          font-size: 10px;
          text-transform: uppercase;
          letter-spacing: 1.2px;
          color: var(--accent);
          border-bottom: 1.5px solid var(--rule);
          padding-bottom: 1px;
          margin: 0 0 3px;
        }
        /* Justified, matching PdfResumeRenderer.ComposeJustified: a full last line instead of
           a ragged right edge, at the cost of nothing but word spacing. */
        .summary, .bullets li { text-align: justify; }
        .summary { margin: 0; color: var(--ink); }
        .skills { margin: 0; }
        .skill-row { margin: 0.5px 0; color: var(--ink); }
        .skill-label { font-weight: 600; }
        .entry { margin-bottom: 3px; page-break-inside: avoid; }
        .entry:last-child { margin-bottom: 0; }
        .entry-head { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; }
        .entry-head h3 { margin: 0; font-size: 10.5px; font-weight: 700; }
        .entry-head .org { font-weight: 500; color: var(--ink); }
        .entry-head .loc { font-weight: 400; color: var(--muted); }
        .dates { flex: 0 0 auto; color: var(--muted); font-size: 9px; white-space: nowrap; }
        .location, .tagline, .links { margin: 0.5px 0 1px; color: var(--muted); font-size: 9px; }
        .links a { color: var(--muted); }
        .bullets { margin: 1px 0 0; padding-left: 16px; }
        /* Chromium and Firefox minimize excessively short last lines with this; it is the one
           lever the HTML path has that the PDF path does not, and it costs nothing when
           unsupported. It is a refinement on top of writing bullets to length, not a
           replacement for it — see BulletLineFit. */
        .bullets li { margin: 0.5px 0; text-wrap: pretty; }
        .certs { margin: 0; padding-left: 16px; }
        .certs li { margin: 1px 0; }
        @media print {
          /* Matches PdfResumeRenderer's geometry: 0.5in margins, 10pt body — the dense
             reference layout this renderer follows. */
          @page { size: Letter; margin: 0.5in; }
          body { font-size: 10pt; line-height: 1.15; }
          .resume { max-width: none; padding: 0; margin: 0; }
          .header h1 { font-size: 15pt; }
          .headline { font-size: 9.5pt; }
          .contact { font-size: 8pt; }
          a { color: inherit; }
        }
        """;
}
