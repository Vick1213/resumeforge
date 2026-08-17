using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Rendering;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Rendering;

/// <summary>Tests for <see cref="PdfResumeRenderer"/>.</summary>
public sealed class PdfResumeRendererTests
{
    private readonly PdfResumeRenderer _renderer = new();

    [Fact]
    public void Produces_a_nonempty_pdf_starting_with_the_pdf_magic_bytes()
    {
        var result = _renderer.Render(RenderingTestData.Document());

        result.Content.ShouldNotBeEmpty();
        Encoding.ASCII.GetString(result.Content, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void Reports_a_page_count_of_at_least_one()
    {
        var result = _renderer.Render(RenderingTestData.Document());

        result.PageCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Grows_a_sparse_document_to_fill_the_page_it_already_occupies()
    {
        // Nothing but a name: as much empty page as this layout can produce, so the fit
        // search has every reason to spend its whole range.
        var sparse = TestData.Document(basics: TestData.Basics("Jordan Rivera"), sectionOrder: []);

        var result = _renderer.Render(sparse);

        result.PageCount.ShouldBe(1);
        result.Scale.ShouldBeGreaterThan(1.0f);
        result.Spacing.ShouldBeGreaterThan(1.0f);
    }

    [Fact]
    public void Never_shrinks_an_overlong_document_to_win_back_a_page()
    {
        // Far more content than one page holds. Fitting must not scale it down to squeeze
        // under a page budget: excluding the lowest-scoring entries is PageBudgetEnforcer's
        // job (CONTRACTS.md §6), and quietly reducing the type instead would leave that
        // contract's deterministic cut order unreachable.
        var overlong = RenderingTestData.Document() with
        {
            Projects = [.. Enumerable.Range(0, 60).Select(i => TestData.Project(
                $"prj:filler-{i}", $"Filler Project {i}", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1),
                bullets: [TestData.Bullet($"prj:filler-{i}#0", "Shipped a service handling a large volume of traffic daily.")]))],
        };

        var result = _renderer.Render(overlong);

        result.PageCount.ShouldBeGreaterThan(1);
        result.Scale.ShouldBeGreaterThanOrEqualTo(1.0f);
        result.Spacing.ShouldBeGreaterThanOrEqualTo(1.0f);
    }

    [Fact]
    public void Renders_successfully_with_every_section_populated()
    {
        var project = TestData.Project(
            "prj:widget", "Widget Tool", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1),
            bullets: [TestData.Bullet("prj:widget#0", "Built a CLI tool used by 200 developers.")], tagline: "A handy CLI.");
        var education = TestData.Education("edu:uw", "University of Washington", "B.S. Computer Science", new DateOnly(2014, 9, 1), new DateOnly(2018, 6, 1));
        var certification = TestData.Certification("cert:cka", "CKA", issuer: "CNCF");

        var document = RenderingTestData.Document() with
        {
            Projects = [project],
            Education = [education],
            Certifications = [certification],
        };

        var result = _renderer.Render(document);

        result.Content.ShouldNotBeEmpty();
    }

    [Fact]
    public void Renders_successfully_for_a_minimal_document_with_no_optional_sections()
    {
        var document = TestData.Document(sectionOrder: [SectionKind.Summary]);

        var result = _renderer.Render(document);

        result.Content.ShouldNotBeEmpty();
        Encoding.ASCII.GetString(result.Content, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void Header_contact_fields_render_as_hyperlinks_to_their_full_target_urls()
    {
        var basics = TestData.Basics(
            "Jordan Rivera",
            email: "jordan@example.com",
            website: "https://jordanrivera.dev",
            linkedIn: "https://www.linkedin.com/in/jordanrivera",
            gitHub: "https://github.com/jordanrivera");
        var document = RenderingTestData.Document() with { Basics = basics };

        var result = _renderer.Render(document);
        var raw = RawText(result.Content);

        // QuestPDF writes link annotations as literal `/URI (...)` strings, uncompressed, so
        // a plain byte search for the full target url is an honest way to assert clickability.
        raw.ShouldContain("mailto:jordan@example.com");
        raw.ShouldContain("https://jordanrivera.dev");
        raw.ShouldContain("https://www.linkedin.com/in/jordanrivera");
        raw.ShouldContain("https://github.com/jordanrivera");
    }

    [Fact]
    public void A_project_with_a_url_and_a_repo_url_renders_hyperlinks_to_both()
    {
        var project = TestData.Project(
            "prj:widget", "Widget Tool",
            bullets: [TestData.Bullet("prj:widget#0", "Built a CLI tool used by 200 developers.")],
            url: "https://widget.example.com", repoUrl: "https://github.com/jordan/widget");
        var document = RenderingTestData.Document() with { Projects = [project] };

        var result = _renderer.Render(document);
        var raw = RawText(result.Content);

        raw.ShouldContain("https://widget.example.com");
        raw.ShouldContain("https://github.com/jordan/widget");
    }

    // QuestPDF embeds a subsetted font whose glyph indices are arbitrary per document, so
    // rendered words (e.g. "Bare Project") never appear as literal text in the PDF bytes —
    // not even after decompressing content streams — the way the un-subsetted `/URI` link
    // annotations do. So presence/absence of a project's *content* is asserted indirectly:
    // render is deterministic (confirmed below), so a contentless entry that is truly
    // skipped produces byte-for-byte the same document as one with no projects at all, and
    // an entry that does render changes the bytes. Timestamps are normalized first since
    // `CreationDate`/`ModDate` otherwise differ between two separate render calls.
    [Fact]
    public void Omits_a_project_with_no_bullets_and_no_tagline()
    {
        var bare = TestData.Project("prj:bare", "Bare Project", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1));
        var withBareProject = RenderingTestData.Document() with { Projects = [bare] };
        var withNoProjects = RenderingTestData.Document() with { Projects = [] };

        var withBareProjectResult = _renderer.Render(withBareProject);
        var withNoProjectsResult = _renderer.Render(withNoProjects);

        NormalizedText(withBareProjectResult.Content).ShouldBe(NormalizedText(withNoProjectsResult.Content));
    }

    [Fact]
    public void Includes_a_project_with_a_tagline_but_no_bullets()
    {
        var taglineOnly = TestData.Project("prj:tagline", "Tagline Project", tagline: "A one-liner with no bullets.");
        var withTaglineProject = RenderingTestData.Document() with { Projects = [taglineOnly] };
        var withNoProjects = RenderingTestData.Document() with { Projects = [] };

        var withTaglineProjectResult = _renderer.Render(withTaglineProject);
        var withNoProjectsResult = _renderer.Render(withNoProjects);

        NormalizedText(withTaglineProjectResult.Content).ShouldNotBe(NormalizedText(withNoProjectsResult.Content));
    }

    [Fact]
    public void Rendering_the_same_document_twice_produces_identical_bytes_once_timestamps_are_normalized()
    {
        var first = _renderer.Render(RenderingTestData.Document());
        var second = _renderer.Render(RenderingTestData.Document());

        NormalizedText(first.Content).ShouldBe(NormalizedText(second.Content));
    }

    // "Software Entity" is deliberately chosen: "ft" and "ti" are the exact pairs an ATS
    // extractor was seen mangling ("So�ware", "En�ty") because a ligature glyph usually has
    // no ToUnicode mapping back to its source letters. A document with nothing but this name
    // and no sections renders no other text, so every character — letters and the space
    // alike — must show up as its own `<hex> Tj` glyph operator in the (Flate-compressed)
    // page content stream if ligatures are truly off. Confirmed by hand that this same setup
    // without `DisableFontFeature` undercounts (13 operators instead of 15), so the equality
    // below is a real, discriminating check, not one that would pass either way.
    [Fact]
    public void Disables_ligatures_so_every_character_gets_its_own_glyph()
    {
        const string name = "Software Entity";
        var document = TestData.Document(basics: TestData.Basics(name), sectionOrder: []);

        var result = _renderer.Render(document);

        CountGlyphShowOperators(result.Content).ShouldBe(name.Length);
    }

    private static string RawText(byte[] content) => Encoding.Latin1.GetString(content);

    private static string NormalizedText(byte[] content) =>
        Regex.Replace(RawText(content), @"D:\d{14}[+-]\d{2}'\d{2}'", "D:TIMESTAMP");

    /// <summary>
    /// Counts `&lt;hex&gt; Tj` glyph-show operators across every Flate-compressed stream in
    /// a PDF (page content, mainly; embedded font programs happen to use filters this doesn't
    /// decompress and are silently skipped, contributing zero).
    /// </summary>
    private static int CountGlyphShowOperators(byte[] pdfContent)
    {
        var raw = RawText(pdfContent);
        var total = 0;
        var index = 0;

        while (true)
        {
            var streamStart = raw.IndexOf("stream", index, StringComparison.Ordinal);
            if (streamStart < 0)
            {
                break;
            }

            var contentStart = streamStart + "stream".Length;
            if (pdfContent[contentStart] == '\r')
            {
                contentStart++;
            }

            if (pdfContent[contentStart] == '\n')
            {
                contentStart++;
            }

            var streamEnd = raw.IndexOf("endstream", contentStart, StringComparison.Ordinal);
            if (streamEnd < 0)
            {
                break;
            }

            total += CountTjInFlateStream(pdfContent[contentStart..streamEnd]);
            index = streamEnd + "endstream".Length;
        }

        return total;
    }

    private static int CountTjInFlateStream(byte[] streamBytes)
    {
        try
        {
            using var input = new MemoryStream(streamBytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);

            return Regex.Matches(RawText(output.ToArray()), @"<[0-9A-Fa-f]+>\s*Tj").Count;
        }
        catch (InvalidDataException)
        {
            // Not a Flate stream (e.g. an embedded font program using a different filter) —
            // nothing here is glyph-show text.
            return 0;
        }
    }
}
