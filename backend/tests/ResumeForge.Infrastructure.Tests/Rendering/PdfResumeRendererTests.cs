using System.Text;
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
}
