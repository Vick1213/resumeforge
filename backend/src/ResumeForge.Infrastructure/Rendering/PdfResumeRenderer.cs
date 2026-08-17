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

    /// <summary>
    /// The uniform zoom levels the fit search may choose from, ascending. A resume whose
    /// content stops well short of the bottom of its last page reads as an unfinished
    /// document rather than a concise one, and one that spills a few lines onto an otherwise
    /// empty page reads worse still. Scaling the whole composition — type, spacing, and rules
    /// together — keeps every proportion the layout was designed with, which independently
    /// nudging font sizes would not.
    /// </summary>
    /// <remarks>
    /// Quantized rather than continuous so the chosen zoom is reproducible and reviewable:
    /// the same document always renders at the same step. Beyond roughly +20% the page stops
    /// looking like the designed layout and starts looking like a photocopier setting.
    ///
    /// Starts at 1.0 and only grows. Shrinking below the authored size would let an
    /// over-long resume squeeze itself under the page budget, which is the page-budget
    /// enforcer's job to solve by excluding the lowest-scoring entries (CONTRACTS.md §6) —
    /// quietly reducing the type instead would leave that contract's deterministic cut order
    /// unreachable. Fitting therefore never changes how many pages a document needs; it only
    /// decides how well it occupies them.
    /// </remarks>
    private static readonly float[] FitScales = [1.00f, 1.04f, 1.08f, 1.12f, 1.16f, 1.20f];

    /// <summary>
    /// Multipliers on <see cref="BaseItemSpacing"/> the fit search may choose from, ascending.
    /// </summary>
    /// <remarks>
    /// Zoom alone cannot close a gap at the foot of the page, because growing the type also
    /// narrows the text box: lines rewrap, the content grows taller than the zoom implies, and
    /// the spill point arrives while whitespace remains. Spacing has no such coupling — it
    /// absorbs leftover height without touching a single line break — so it is searched second,
    /// after zoom has been maximized. It applies between top-level items (sections, entries),
    /// never within a bullet list, so the air lands where a typesetter would put it.
    /// </remarks>
    private static readonly float[] FitSpacings = [1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f];

    /// <summary>The gap between top-level column items at spacing multiplier <c>1.0</c>.</summary>
    private const float BaseItemSpacing = 2f;

    /// <summary>Renders <paramref name="doc"/> to PDF, alongside the resulting page count.</summary>
    /// <remarks>
    /// The layout is chosen by <see cref="ChooseFit"/> so the content fills its last page
    /// instead of leaving a ragged gap. Page count is reported after fitting, and fitting never
    /// increases it, so callers that budget by page count (the page-budget enforcer) see the
    /// same number they would have without it.
    /// </remarks>
    public PdfRenderResult Render(ResumeDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var fit = ChooseFit(doc);
        var document = BuildDocument(doc, fit.Scale, fit.Spacing);

        return new PdfRenderResult
        {
            Content = document.GeneratePdf(),
            PageCount = fit.PageCount,
            Scale = fit.Scale,
            Spacing = fit.Spacing,
        };
    }

    private readonly record struct FitChoice(float Scale, float Spacing, int PageCount);

    /// <summary>
    /// Picks the largest zoom, then the most generous spacing, that still fits the fewest
    /// pages the document can occupy.
    /// </summary>
    /// <remarks>
    /// The authored layout establishes the page count to aim for, and the searches then spend
    /// whatever room is left on that same number of pages. Zoom is maximized before spacing
    /// because type size carries legibility while spacing only carries air — spending the slack
    /// the other way round would yield a small resume swimming in white.
    ///
    /// Both searches are binary, which requires page count to be monotonic in each dimension:
    /// neither a larger zoom nor a wider gap can ever make a document need *fewer* pages.
    /// </remarks>
    private FitChoice ChooseFit(ResumeDocument doc)
    {
        var targetPages = CountPages(doc, FitScales[0], FitSpacings[0]);

        var scale = FitScales[LargestFitting(
            FitScales.Length, i => CountPages(doc, FitScales[i], FitSpacings[0]) <= targetPages)];

        var spacing = FitSpacings[LargestFitting(
            FitSpacings.Length, i => CountPages(doc, scale, FitSpacings[i]) <= targetPages)];

        return new FitChoice(scale, spacing, targetPages);
    }

    /// <summary>
    /// The highest index in <c>[0, count)</c> that satisfies <paramref name="fits"/>, assuming
    /// the predicate is monotonically decreasing (true up to some index, false after). Index 0
    /// is the floor: it is the tightest option and is returned untested when nothing else fits.
    /// </summary>
    private static int LargestFitting(int count, Func<int, bool> fits)
    {
        var low = 1;
        var high = count - 1;
        var best = 0;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (fits(mid))
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private int CountPages(ResumeDocument doc, float scale, float spacing) =>
        BuildDocument(doc, scale, spacing)
            .GenerateImages(new ImageGenerationSettings { RasterDpi = PageCountRasterDpi })
            .Count();

    private static IDocument BuildDocument(ResumeDocument doc, float scale, float spacing)
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

                // Scale wraps the whole content box: QuestPDF lays the column out in a
                // correspondingly narrower/wider space and then draws it zoomed, so type,
                // spacing, and rules all grow together and the margins stay where the page
                // set them. Applied here rather than to individual elements so no composer
                // needs to know the fit search exists.
                page.Content().Scale(scale).Column(column =>
                {
                    column.Spacing(BaseItemSpacing * spacing);

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

    /// <summary>
    /// The uniform zoom the fit search settled on (<c>1.0</c> = the layout as authored), and
    /// the multiplier it applied to the gap between top-level items. Exposed because "why is
    /// this resume set larger than that one?" is otherwise unanswerable from the output alone.
    /// </summary>
    public required float Scale { get; init; }

    /// <inheritdoc cref="Scale"/>
    public required float Spacing { get; init; }
}
