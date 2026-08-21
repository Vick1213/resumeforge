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
    /// <summary>
    /// Every glyph on the page is black. Grey secondary text and a blue accent read well on a
    /// screen and cost the document on paper and in a scan: r/EngineeringResumes states it
    /// outright — "Don't use grey-colored fonts since they're hard to read. Stick to black.
    /// Make sure to use a color that is printable in grey-scale." Hierarchy is carried by
    /// weight, size, and position instead, which survives both a photocopier and a
    /// colour-blind reader. <see cref="RuleColor"/> is the one exception, and it is not text.
    /// </summary>
    private static readonly string InkColor = Colors.Black;
    private static readonly string MutedColor = Colors.Black;
    private static readonly string AccentColor = Colors.Black;
    private static readonly string RuleColor = Colors.Grey.Lighten1;

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

    /// <summary>The page margin, in inches, on all four sides.</summary>
    /// <remarks>See the note at <c>page.Margin</c> in <see cref="BuildDocument"/>.</remarks>
    private const float PageMarginInches = 0.5f;

    /// <summary>The body text size, in points.</summary>
    /// <remarks>
    /// 10pt, matching the dense single-page reference resumes this layout follows: they set
    /// their body near 10pt and spend the space saved on more bullets per entry. Career-center
    /// guides allow 10-12pt; the floor of that range is deliberate — the page should be full
    /// of content, not of type.
    /// </remarks>
    private const float BodyFontSize = 10f;

    /// <summary>
    /// Sizes for the text that is not body copy, all derived from <see cref="BodyFontSize"/>
    /// so the page rescales as one thing. The name leads, section headings and entry titles
    /// sit a step above the body, and the contact line and secondary metadata a step below.
    /// </summary>
    /// <remarks>
    /// The header is deliberately modest: at <c>+8</c> the name, headline, and two contact
    /// rows together consumed a seventh of the page before the first section began, which
    /// on a dense single-page resume is space bullets should be carrying. The reference
    /// layouts set the name at roughly 1.5× body and everything else in the header a step
    /// below body, and that is what these follow.
    /// </remarks>
    private const float NameFontSize = BodyFontSize + 5f;
    private const float HeadlineFontSize = BodyFontSize - 0.5f;
    private const float SectionHeadingFontSize = BodyFontSize + 0.5f;
    private const float SecondaryFontSize = BodyFontSize - 1.5f;

    /// <summary>
    /// The contact rows' type size, a step under <see cref="SecondaryFontSize"/>: the header
    /// carries up to six fields — three of them URLs — and the smaller the type, the more of
    /// them fit on the single line the header should cost. 7.5pt matches the contact line of
    /// the reference layouts, which set all six fields across one line.
    /// </summary>
    private const float ContactFontSize = BodyFontSize - 2.5f;

    /// <summary>
    /// The right-aligned date column's width. Scales with <see cref="BodyFontSize"/> so
    /// raising the type does not push a date onto a second line. Sized for the longest range
    /// <see cref="DateRangeFormatter"/> produces ("Sept 2025 – Sept 2026") and no wider:
    /// every point it takes is a point the entry title on its left does not get, and a title
    /// that wraps costs a whole line where the date column's slack costs nothing.
    /// </summary>
    private const float DateColumnWidth = BodyFontSize * 11f;

    /// <summary>The width reserved for a bullet's marker glyph and the gap after it.</summary>
    private const float BulletMarkerWidth = BodyFontSize * 1.15f;

    /// <summary>
    /// The uniform zoom levels the fit search may choose from. Fixed at 1.0: zooming a thin
    /// resume up to fill its page made it read as "empty but magnified" — big type carrying
    /// little content — which is the opposite of the dense reference layouts this renderer
    /// follows. A page that stops short of its foot is a signal that content is missing, and
    /// the fix belongs upstream (include more bullets and entries), never in the type size.
    /// </summary>
    /// <remarks>
    /// The array and the search machinery stay so a future scale step is one edit away, and
    /// because <see cref="PdfRenderResult.Scale"/> is part of the render contract.
    /// </remarks>
    private static readonly float[] FitScales = [1.00f];

    /// <summary>
    /// Multipliers on <see cref="BaseItemSpacing"/> the fit search may choose from, ascending.
    /// </summary>
    /// <remarks>
    /// Spacing absorbs leftover height without touching a single line break, so it is the one
    /// dimension the fit search still spends. Capped at 2.0×: past that the air between
    /// sections stops reading as typesetting and starts reading as padding — the sparse look
    /// the dense reference layouts exist to avoid. A last page that 2.0× cannot fill stays
    /// honestly short instead of being stretched.
    /// </remarks>
    private static readonly float[] FitSpacings = [1.0f, 1.25f, 1.5f, 1.75f, 2.0f];

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

                // 0.5in margins: the floor every source that publishes one agrees on —
                // r/EngineeringResumes ("minimum 0.4 inches"), CMU SCS ("no smaller than
                // 0.5\""), Harvard, Berkeley, MIT. The previous 0.25in setting cleared none
                // of them, and it also stretched a bullet line far past the 45-90 characters
                // typography treats as readable.
                page.Margin(PageMarginInches, Unit.Inch);

                // Ligatures ("fi", "ft", "ti"...) are a display nicety that subsetted PDF
                // fonts pay for at ATS's expense: the glyph a ligature draws often has no
                // ToUnicode mapping back to its source letters, so "Software" and "Entity"
                // extract as "So�ware" and "En�ty". Disabled globally — every text element in
                // the document inherits this — since no amount of visual polish is worth an
                // ATS parser mangling the resume's own words.
                page.DefaultTextStyle(x => x.FontSize(BodyFontSize).FontColor(InkColor).LineHeight(BaseLineHeight)
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
        // The whole header is one top-level item with no internal spacing: the fit search's
        // spacing multiplier belongs between sections and entries, and letting it open gaps
        // between a name and its own contact line was a good part of how the header grew to
        // a seventh of the page.
        column.Item().Column(header =>
        {
            header.Spacing(0);

            header.Item().Text(basics.FullName).FontSize(NameFontSize).Bold().FontColor(InkColor).AlignCenter();

            if (!string.IsNullOrWhiteSpace(basics.Headline))
            {
                header.Item().Text(basics.Headline).FontSize(HeadlineFontSize).SemiBold().FontColor(AccentColor).AlignCenter();
            }

            // Split deliberately rather than letting the line wrap: a wrap chosen by the
            // layout engine lands mid-URL, which reads as a broken document rather than a
            // full one. At ContactFontSize a full six-field header (three of them URLs)
            // fits one line; identity fields go first and links second when it does not,
            // so a second row looks like a decision.
            var identity = ContactParts(basics, links: false).ToList();
            var links = ContactParts(basics, links: true).ToList();

            if (FitsOneContactRow(identity.Count + links.Count, [.. identity, .. links]))
            {
                ComposeContactRow(header, [.. identity, .. links]);
                return;
            }

            ComposeContactRow(header, identity);
            ComposeContactRow(header, links);
        });
    }

    /// <summary>
    /// Whether every contact field fits on one centered line, measured rather than counted:
    /// the joined display text — fields plus the separator between each pair — against the
    /// character budget one line of <see cref="SecondaryFontSize"/> type holds across a
    /// Letter page's content width.
    /// </summary>
    /// <remarks>
    /// The rule used to be "four fields or fewer", which split a header carrying six short
    /// ones (a city, a phone number, and four scheme-stripped hostnames) that a single line
    /// holds comfortably, and cost a line to do it. Length is what actually decides whether a
    /// line wraps, so length is what is checked. The budget is deliberately conservative:
    /// overshooting hands the wrap point to the layout engine, which lands it mid-URL.
    /// </remarks>
    private static bool FitsOneContactRow(int fieldCount, IReadOnlyList<(string Display, string? Href)> parts)
    {
        if (fieldCount == 0)
        {
            return true;
        }

        var length = parts.Sum(p => p.Display.Length) + (ContactSeparator.Length * (fieldCount - 1));
        return length <= SingleRowContactCharBudget;
    }

    /// <summary>
    /// Roughly how many characters of <see cref="ContactFontSize"/> type one centered line
    /// holds across the content width a Letter page leaves inside
    /// <see cref="PageMarginInches"/> margins. Scaled from the same wrap measurement behind
    /// <see cref="Domain.Formatting.BulletLineFit.CharsPerLine"/> (~110 wide-case characters
    /// at 10pt → ~146 at 7.5pt), then held under it so a header of unusually wide characters
    /// still clears the line. Contact text — hostnames, digits, an email — runs narrower
    /// than the mixed-case prose that measurement used, so the margin is real.
    /// </summary>
    private const int SingleRowContactCharBudget = 142;

    /// <summary>What sits between two adjacent contact fields on the same row.</summary>
    private const string ContactSeparator = " | ";

    private static void ComposeContactRow(ColumnDescriptor column, IReadOnlyList<(string Display, string? Href)> parts)
    {
        if (parts.Count == 0)
        {
            return;
        }

        column.Item().Text(text =>
        {
            text.AlignCenter();

            // Every span in this block — plain or hyperlinked — carries the same size and
            // color, so turning a field into a link changes nothing but its clickability.
            text.DefaultTextStyle(x => x.FontSize(ContactFontSize).FontColor(MutedColor));

            for (var i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    text.Span(ContactSeparator);
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

    /// <summary>
    /// The header contact fields in display order, each paired with the URL it should link to
    /// (a <c>mailto:</c> link for email, the full original URL for website/LinkedIn/GitHub),
    /// or a null href for fields that stay plain text (phone, location). Split into the two
    /// rows the header may use: <paramref name="links"/> selects the website/LinkedIn/GitHub
    /// group, otherwise the email/phone/location group.
    /// </summary>
    private static IEnumerable<(string Display, string? Href)> ContactParts(ResumeBasics basics, bool links)
    {
        if (links)
        {
            foreach (var url in new[] { basics.Website, basics.LinkedIn, basics.GitHub })
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    yield return (ForDisplay(url), url.Trim());
                }
            }

            yield break;
        }

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

    /// <summary>
    /// Composes a section heading and the rule under it as a <em>single</em> top-level item.
    /// </summary>
    /// <remarks>
    /// The two used to be separate items, which put the fit search's spacing multiplier
    /// between the heading and its own rule, and again between that rule and the section's
    /// first line. At the wide end of <see cref="FitSpacings"/> that reads as a heading
    /// floating free of everything it labels — air is only worth spending between things
    /// that are genuinely separate. Nesting them fixes their relationship at the authored
    /// gap regardless of what multiplier fills the page.
    /// </remarks>
    private static void ComposeSectionHeading(ColumnDescriptor column, string title)
    {
        column.Item().EnsureSpace(KeepWithNextHeight).PaddingTop(1).Column(heading =>
        {
            heading.Item().Text(title.ToUpperInvariant()).FontSize(SectionHeadingFontSize).Bold().FontColor(AccentColor);
            heading.Item().PaddingBottom(0.5f).LineHorizontal(0.5f).LineColor(RuleColor);
        });
    }

    private static void ComposeSummary(ColumnDescriptor column, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        ComposeSectionHeading(column, "Summary");
        ComposeJustified(column, summary.Replace('\n', ' '));
    }

    private static void ComposeSkills(ColumnDescriptor column, IReadOnlyList<SkillGroup> groups)
    {
        var included = groups.Where(g => g.Included && g.Items.Count > 0).ToList();
        if (included.Count == 0)
        {
            return;
        }

        ComposeSectionHeading(column, "Skills");

        // The whole block is one item: consecutive skill rows are a list, not a sequence of
        // sections, and the fit search's spacing multiplier between them would pull a
        // five-row block apart down the page — the same reasoning as ComposeSectionHeading.
        column.Item().Column(skills =>
        {
            foreach (var group in included)
            {
                // The label leads the same line its skills run on, rather than sitting in a
                // fixed left column. A column wide enough for "Cloud & Infrastructure"
                // reserves that width on every row, including the ones labeled "Other", and
                // then wraps the skills into the narrow remainder — which is how a five-group
                // skills block grew to eat a third of the page. Inline, each group costs the
                // lines its own content needs and nothing more, and the block reads as the
                // dense reference footer a skills section is supposed to be.
                skills.Item().Text(text =>
                {
                    text.Span(group.Label + ": ").Bold();

                    foreach (var skill in group.Items)
                    {
                        var span = text.Span(skill.Name + (skill == group.Items[^1] ? string.Empty : ", "));
                        if (skill.Emphasized)
                        {
                            span.Bold();
                        }
                    }
                });
            }
        });
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
            column.Item().EnsureSpace(KeepWithNextHeight).Column(entryColumn =>
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
                    row.ConstantItem(DateColumnWidth).AlignRight().Text(DateRangeFormatter.Format(entry.StartDate, entry.EndDate)).FontColor(MutedColor);
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
            column.Item().EnsureSpace(KeepWithNextHeight).Column(entryColumn =>
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

                            text.Span("   ·   ").FontSize(SecondaryFontSize).FontColor(MutedColor);
                            text.Hyperlink(ForDisplay(url), url.Trim()).FontSize(SecondaryFontSize).FontColor(MutedColor);
                        }
                    });

                    var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
                    if (range is not null)
                    {
                        row.ConstantItem(DateColumnWidth).AlignRight().Text(range).FontColor(MutedColor);
                    }
                });

                if (entry.ShouldRenderTagline(out var tagline))
                {
                    // Italic gives a bullet-less project entry a visible rhythm: bold name
                    // (+ links), italic tagline.
                    entryColumn.Item().Text(tagline).FontSize(SecondaryFontSize).FontColor(MutedColor).Italic();
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
            column.Item().EnsureSpace(KeepWithNextHeight).Column(entryColumn =>
            {
                entryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text($"{entry.Credential} — {entry.Institution}").Bold();

                    var range = FormatOptionalRange(entry.StartDate, entry.EndDate);
                    if (range is not null)
                    {
                        row.ConstantItem(DateColumnWidth).AlignRight().Text(range).FontColor(MutedColor);
                    }
                });

                var meta = EducationMetaLine(entry);
                if (meta is not null)
                {
                    entryColumn.Item().Text(meta).FontSize(SecondaryFontSize).FontColor(MutedColor).Italic();
                }
            });
        }
    }

    /// <summary>
    /// An education entry's whole secondary line — location, GPA, and every highlight —
    /// joined with " · ", or null when the entry has none of them.
    /// </summary>
    /// <remarks>
    /// Highlights used to render as bullets under the entry, which gave a single line of
    /// coursework the same visual weight, and the same vertical cost, as a bullet describing
    /// a year of work. Folded into the metadata line they read the way every reference
    /// format sets them — "GPA: 3.5 · Coursework: …" — and a degree costs two lines instead
    /// of four.
    /// </remarks>
    internal static string? EducationMetaLine(EducationEntry entry)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.Location))
        {
            parts.Add(entry.Location.Trim());
        }

        if (entry.Gpa is { } gpa)
        {
            parts.Add($"GPA: {gpa.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        parts.AddRange(entry.Highlights.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()));

        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
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
            var issued = entry.IssuedOn is { } issuedOn ? DateRangeFormatter.FormatMonth(issuedOn) : null;
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
            // A bullet is one or two lines by construction (see BulletLineFit), so keeping it
            // whole can never strand more than that, and a bullet broken across a page break
            // is unreadable in exactly the place a reader is least willing to work for it.
            entryColumn.Item().PreventPageBreak().Row(row =>
            {
                row.ConstantItem(BulletMarkerWidth).Text("•");
                ComposeJustified(row.RelativeItem(), bulletText);
            });
        }
    }

    /// <summary>
    /// Sets <paramref name="text"/> justified — flush on both edges rather than ragged-right.
    /// </summary>
    /// <remarks>
    /// A justified line carries every word it can before wrapping, so a bullet written near
    /// the top of a <see cref="BulletLineFit"/> band keeps its last line full instead of
    /// stranding two words on it; across a page of bullets that is the difference between
    /// filling the column and leaving a ragged edge down the right of the document. It also
    /// matches how the reference layouts this renderer follows set their body copy. Word
    /// spacing is the only thing it stretches — no line breaks move, and no glyph changes —
    /// so <see cref="BulletLineFit"/>'s character estimate stays exactly as calibrated.
    /// </remarks>
    private static void ComposeJustified(IContainer container, string text) =>
        container.Text(descriptor =>
        {
            descriptor.Justify();
            descriptor.Span(text);
        });

    /// <inheritdoc cref="ComposeJustified(IContainer, string)"/>
    private static void ComposeJustified(ColumnDescriptor column, string text) =>
        ComposeJustified(column.Item(), text);

    /// <summary>
    /// The height an entry — or a section heading — must have available before it may start on
    /// the current page. Below it, the element moves to the next page rather than leaving a
    /// heading or a job title stranded at the foot of this one with nothing under it.
    /// Approximately a title row plus two bullet lines.
    /// </summary>
    private static float KeepWithNextHeight => BodyFontSize * BaseLineHeight * 3.2f;

    private static string? FormatOptionalRange(DateOnly? start, DateOnly? end)
    {
        if (start is { } s)
        {
            return DateRangeFormatter.Format(s, end);
        }

        return end is { } e ? DateRangeFormatter.FormatMonth(e) : null;
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
