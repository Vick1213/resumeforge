using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Rendering;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Rendering;

/// <summary>Tests for <see cref="HtmlResumeRenderer"/>.</summary>
public sealed class HtmlResumeRendererTests
{
    private readonly HtmlResumeRenderer _renderer = new();

    [Fact]
    public void Produces_a_self_contained_document_with_embedded_css()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldStartWith("<!doctype html>");
        html.ShouldContain("<style>");
        html.ShouldNotContain("<link ");
        html.ShouldNotContain("<script ");
    }

    [Fact]
    public void Includes_a_print_media_query_with_page_margins()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldContain("@media print");
        html.ShouldContain("@page");
    }

    [Fact]
    public void Includes_content_from_included_entries()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldContain("Senior Engineer");
        html.ShouldContain("Cut checkout latency 8x.");
    }

    [Fact]
    public void Omits_excluded_experience_entries()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldNotContain("OldCo");
        html.ShouldNotContain("Should never appear in output.");
    }

    [Fact]
    public void Omits_excluded_skill_groups()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldNotContain("Soft Skills");
    }

    [Fact]
    public void Html_encodes_special_characters_in_user_supplied_text()
    {
        var document = RenderingTestData.Document() with
        {
            Basics = RenderingTestData.Document().Basics with { FullName = "A & B <Test>" },
        };

        var html = _renderer.Render(document);

        html.ShouldContain("A &amp; B &lt;Test&gt;");
        html.ShouldNotContain("A & B <Test>");
    }

    [Fact]
    public void Respects_a_custom_section_order()
    {
        var document = RenderingTestData.Document([SectionKind.Experience, SectionKind.Summary]);

        var html = _renderer.Render(document);

        html.IndexOf("Experience</h2>", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Summary</h2>", StringComparison.Ordinal));
    }

    [Fact]
    public void Emphasized_skills_render_inside_a_strong_tag()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldContain("<strong>C#</strong>");
    }
}
