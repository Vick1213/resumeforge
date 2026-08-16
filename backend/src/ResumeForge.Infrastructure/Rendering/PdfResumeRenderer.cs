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

    /// <summary>
    /// The raster DPI used purely to count pages (via QuestPDF's per-page image
    /// generation). Deliberately low, since only the number of images is read, never their
    /// pixels.
    /// </summary>
    private const int PageCountRasterDpi = 72;

    /// <summary>
    /// The document-wide line height multiplier. 1.0 read as visibly cramped; no test in this
    /// suite pins a page count against real rendered content, so 1.15 (the top of the
    /// requested 1.05–1.15 range) is used outright rather than backed off.
    /// </summary>
    private const float BaseLineHeight = 1.15f;

    /// <summary>Renders <paramref name="doc"/> to PDF, alongside the resulting page count.</summary>
    public PdfRenderResult Render(ResumeDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var document = BuildDocument(doc);
        var content = document.GeneratePdf();
        var pageCount = document.GenerateImages(new ImageGenerationSettings { RasterDpi = PageCountRasterDpi }).Count();

        return new PdfRenderResult { Content = content, PageCount = pageCount };
    }

    private static IDocument BuildDocument(ResumeDocument doc)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.25f, Unit.Inch);

                // Ligatures ("fi", "ft", "ti"...) are a display nicety that subsetted PDF
                // fonts pay for at ATS's expense: the glyph a ligature draws often has no
                // ToUnicode mapping back to its source letters, so "Software" and "Entity"
                // extract as "So�ware" and "En�ty". Disabled globally — every text element in
                // the document inherits this — since no amount of visual polish is worth an
                // ATS parser mangling the resume's own words.
                page.DefaultTextStyle(x => x.FontSize(9f).FontColor(InkColor).LineHeight(BaseLineHeight)
                    .DisableFontFeature(FontFeatures.StandardLigatures));

                page.Content().Column(column =>
                {
                    column.Spacing(2f);

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
    }

    private static void ComposeHeader(ColumnDescriptor column, ResumeBasics basics)
    {
        column.Item().Text(basics.FullName).FontSize(17f).Bold().FontColor(InkColor).AlignCenter();

        if (!string.IsNullOrWhiteSpace(basics.Headline))
        {
            column.Item().Text(basics.Headline).FontSize(10f).SemiBold().FontColor(AccentColor).AlignCenter();
        }

        var parts = ContactParts(basics).ToList();
        if (parts.Count > 0)
        {
            column.Item().Text(text =>
            {
                text.AlignCenter();

                // Every span in this block — plain or hyperlinked — carries the same size and
                // color, so turning a field into a link changes nothing but its clickability.
                text.DefaultTextStyle(x => x.FontSize(8f).FontColor(MutedColor));

                for (var i = 0; i < parts.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Span("   ·   ");
                    }

                    var (display, href) = parts[i];
                    if (href is null)
                    {
                        text.Span(display);
                    }
                    else
                    {
                        text.Hyperlink(display, href);
                    }
                }
            });
        }
    }

    /// <summary>
    /// The header contact fields in display order, each paired with the URL it should link to
    /// (a <c>mailto:</c> link for email, the full original URL for website/LinkedIn/GitHub),
    /// or a null href for fields that stay plain text (phone, location).
    /// </summary>
    private static IEnumerable<(string Display, string? Href)> ContactParts(ResumeBasics basics)
    {
        if (!string.IsNullOrWhiteSpace(basics.Email))
        {
            yield return (ForDisplay(basics.Email), $"mailto:{basics.Email.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(basics.Phone))
        {
            yield return (ForDisplay(basics.Phone), null);
        }

        if (!string.IsNullOrWhiteSpace(basics.Location))
        {
            yield return (ForDisplay(basics.Location), null);
        }

        foreach (var url in new[] { basics.Website, basics.LinkedIn, basics.GitHub })
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                yield return (ForDisplay(url), url.Trim());
            }
        }
    }

    /// <summary>
    /// Strips the scheme and any trailing slash from a URL for the contact line. Printed in
    /// full, three URLs wrap onto a second line and strand a fragment of the last one; nobody
    /// types a scheme off a resume anyway, so it is noise that costs a line.
    /// </summary>
    private static string ForDisplay(string value)
    {
        var trimmed = value.Trim();

        foreach (var scheme in new[] { "https://", "http://" })
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[scheme.Length..];
                break;
            }
        }

        if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        return trimmed.TrimEnd('/');
    }

    private static void ComposeSectionHeading(ColumnDescriptor column, string title)
    {
        column.Item().PaddingTop(1).Text(title.ToUpperInvariant()).FontSize(9f).Bold().FontColor(AccentColor);
        column.Item().PaddingBottom(0.5f).LineHorizontal(0.5f).LineColor(RuleColor);
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
                var titleSuffix = string.IsNullOrWhiteSpace(entry.Location) ? string.Empty : $" · {entry.Location}";

                entryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span($"{entry.Role} — {entry.Organization}").Bold();
                        if (titleSuffix.Length > 0)
                        {
                            text.Span(titleSuffix).FontColor(MutedColor);
                        }
                    });
                    row.ConstantItem(140).AlignRight().Text(DateRangeFormatter.Format(entry.StartDate, entry.EndDate)).FontColor(MutedColor);
                });

                ComposeBullets(entryColumn, entry.Bullets.Select(b => b.Text));
            });
        }
    }

    private static void ComposeProjects(ColumnDescriptor column, IReadOnlyList<ProjectEntry> entries)
    {
        var included = entries.Where(e => e.Included && e.HasRenderableContent()).ToList();
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
                    row.RelativeItem().Text(text =>
                    {
                        text.Span(entry.Name).Bold();

                        foreach (var url in new[] { entry.Url, entry.RepoUrl })
                        {
                            if (string.IsNullOrWhiteSpace(url))
                            {
                                continue;
                            }

                            text.Span("   ·   ").FontSize(8f).FontColor(MutedColor);
                            text.Hyperlink(ForDisplay(url), url.Trim()).FontSize(8f).FontColor(MutedColor);
                        }
                    });

                    var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
                    if (range is not null)
                    {
                        row.ConstantItem(140).AlignRight().Text(range).FontColor(MutedColor);
                    }
                });

                if (!string.IsNullOrWhiteSpace(entry.Tagline))
                {
                    // Italic gives each project entry a visible rhythm: bold name (+ links),
                    // italic tagline, plain bullets.
                    entryColumn.Item().Text(entry.Tagline).FontSize(9).FontColor(MutedColor).Italic();
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

/// <summary>The result of <see cref="PdfResumeRenderer.Render"/>: the PDF bytes and the page count QuestPDF laid them out into.</summary>
public sealed record PdfRenderResult
{
    /// <summary>The rendered PDF bytes.</summary>
    public required byte[] Content { get; init; }

    /// <summary>The number of pages QuestPDF laid the document out into.</summary>
    public required int PageCount { get; init; }
}
