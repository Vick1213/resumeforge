using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Rendering;

/// <summary>
/// Renders a <see cref="ResumeDocument"/> to PDF with QuestPDF, mirroring
/// <see cref="HtmlResumeRenderer"/>'s layout: header, then each section in
/// <see cref="ResumeDocument.SectionOrder"/>, skipping anything excluded.
/// </summary>
public sealed class PdfResumeRenderer
{
    private static readonly string InkColor = Colors.Grey.Darken4;
    private static readonly string MutedColor = Colors.Grey.Darken1;
    private static readonly string AccentColor = Colors.Blue.Darken3;
    private static readonly string RuleColor = Colors.Grey.Lighten2;

    static PdfResumeRenderer()
    {
        // Composition-time license declaration, required once per process by QuestPDF.
        // Guarded here (rather than only in DI startup) so the renderer also works when
        // constructed directly, e.g. in tests.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Renders <paramref name="doc"/> to PDF bytes.</summary>
    public byte[] Render(ResumeDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.55f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(InkColor));

                page.Content().Column(column =>
                {
                    column.Spacing(10);

                    ComposeHeader(column, doc.Basics);

                    foreach (var section in doc.SectionOrder)
                    {
                        switch (section)
                        {
                            case SectionKind.Summary:
                                ComposeSummary(column, doc.Summary);
                                break;
                            case SectionKind.Skills:
                                ComposeSkills(column, doc.Skills);
                                break;
                            case SectionKind.Experience:
                                ComposeExperience(column, doc.Experience);
                                break;
                            case SectionKind.Projects:
                                ComposeProjects(column, doc.Projects);
                                break;
                            case SectionKind.Education:
                                ComposeEducation(column, doc.Education);
                                break;
                            case SectionKind.Certifications:
                                ComposeCertifications(column, doc.Certifications);
                                break;
                        }
                    }
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(ColumnDescriptor column, ResumeBasics basics)
    {
        column.Item().Text(basics.FullName).FontSize(22).Bold().FontColor(InkColor);

        if (!string.IsNullOrWhiteSpace(basics.Headline))
        {
            column.Item().Text(basics.Headline).FontSize(12).SemiBold().FontColor(AccentColor);
        }

        var contact = string.Join("   ·   ", ContactParts(basics));
        if (contact.Length > 0)
        {
            column.Item().Text(contact).FontSize(9.5f).FontColor(MutedColor);
        }
    }

    private static IEnumerable<string> ContactParts(ResumeBasics basics)
    {
        foreach (var value in new[] { basics.Email, basics.Phone, basics.Location, basics.Website, basics.LinkedIn, basics.GitHub })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static void ComposeSectionHeading(ColumnDescriptor column, string title)
    {
        column.Item().PaddingTop(4).Text(title.ToUpperInvariant()).FontSize(10).Bold().FontColor(AccentColor);
        column.Item().PaddingBottom(2).LineHorizontal(1).LineColor(RuleColor);
    }

    private static void ComposeSummary(ColumnDescriptor column, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        ComposeSectionHeading(column, "Summary");
        column.Item().Text(summary.Replace('\n', ' '));
    }

    private static void ComposeSkills(ColumnDescriptor column, IReadOnlyList<SkillGroup> groups)
    {
        var included = groups.Where(g => g.Included && g.Items.Count > 0).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Skills");

        foreach (var group in included)
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(120).Text(group.Label).Bold();
                row.RelativeItem().Text(text =>
                {
                    foreach (var skill in group.Items)
                    {
                        var span = text.Span(skill.Name + (skill == group.Items[^1] ? string.Empty : ", "));
                        if (skill.Emphasized)
                        {
                            span.Bold();
                        }
                    }
                });
            });
        }
    }

    private static void ComposeExperience(ColumnDescriptor column, IReadOnlyList<ExperienceEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Experience");

        foreach (var entry in included)
        {
            column.Item().Column(entryColumn =>
            {
                entryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{entry.Role} — {entry.Organization}").Bold();
                    row.ConstantItem(140).AlignRight().Text(DateRangeFormatter.Format(entry.StartDate, entry.EndDate)).FontColor(MutedColor);
                });

                if (!string.IsNullOrWhiteSpace(entry.Location))
                {
                    entryColumn.Item().Text(entry.Location).FontSize(9).FontColor(MutedColor);
                }

                ComposeBullets(entryColumn, entry.Bullets.Select(b => b.Text));
            });
        }
    }

    private static void ComposeProjects(ColumnDescriptor column, IReadOnlyList<ProjectEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Projects");

        foreach (var entry in included)
        {
            column.Item().Column(entryColumn =>
            {
                entryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text(entry.Name).Bold();

                    var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
                    if (range is not null)
                    {
                        row.ConstantItem(140).AlignRight().Text(range).FontColor(MutedColor);
                    }
                });

                if (!string.IsNullOrWhiteSpace(entry.Tagline))
                {
                    entryColumn.Item().Text(entry.Tagline).FontSize(9).FontColor(MutedColor);
                }

                ComposeBullets(entryColumn, entry.Bullets.Select(b => b.Text));
            });
        }
    }

    private static void ComposeEducation(ColumnDescriptor column, IReadOnlyList<EducationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Education");

        foreach (var entry in included)
        {
            column.Item().Column(entryColumn =>
            {
                entryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{entry.Credential} — {entry.Institution}").Bold();

                    var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
                    if (range is not null)
                    {
                        row.ConstantItem(140).AlignRight().Text(range).FontColor(MutedColor);
                    }
                });

                var meta = entry.Gpa is { } gpa
                    ? $"{entry.Location}   GPA: {gpa.ToString("0.00", CultureInfo.InvariantCulture)}"
                    : entry.Location;

                if (!string.IsNullOrWhiteSpace(meta))
                {
                    entryColumn.Item().Text(meta).FontSize(9).FontColor(MutedColor);
                }

                ComposeBullets(entryColumn, entry.Highlights);
            });
        }
    }

    private static void ComposeCertifications(ColumnDescriptor column, IReadOnlyList<CertificationEntry> entries)
    {
        var included = entries.Where(e => e.Included).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Certifications");

        foreach (var entry in included)
        {
            var issued = entry.IssuedOn is { } issuedOn ? issuedOn.ToString("MMM yyyy", CultureInfo.InvariantCulture) : null;
            var suffix = string.Join(", ", new[] { entry.Issuer, issued }.Where(v => !string.IsNullOrWhiteSpace(v)));

            column.Item().Text(text =>
            {
                text.Span(entry.Name).Bold();
                if (suffix.Length > 0)
                {
                    text.Span(" — " + suffix).FontColor(MutedColor);
                }
            });
        }
    }

    private static void ComposeBullets(ColumnDescriptor entryColumn, IEnumerable<string> bullets)
    {
        foreach (var bulletText in bullets)
        {
            entryColumn.Item().Row(row =>
            {
                row.ConstantItem(10).Text("•");
                row.RelativeItem().Text(bulletText);
            });
        }
    }

    private static string? FormatOptionalRange(DateOnly? start, DateOnly? end)
    {
        if (start is { } s)
        {
            return DateRangeFormatter.Format(s, end);
        }

        return end is { } e ? e.ToString("MMM yyyy", CultureInfo.InvariantCulture) : null;
    }
}
