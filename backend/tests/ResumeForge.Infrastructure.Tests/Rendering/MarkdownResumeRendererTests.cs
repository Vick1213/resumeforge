using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Rendering;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Rendering;

/// <summary>Tests for <see cref="MarkdownResumeRenderer"/>.</summary>
public sealed class MarkdownResumeRendererTests
{
    private readonly MarkdownResumeRenderer _renderer = new();

    [Fact]
    public void Includes_content_from_included_entries()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldContain("Senior Engineer");
        markdown.ShouldContain("Cut checkout latency 8x.");
    }

    [Fact]
    public void Omits_excluded_experience_entries()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldNotContain("OldCo");
        markdown.ShouldNotContain("Should never appear in output.");
    }

    [Fact]
    public void Omits_excluded_skill_groups()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldNotContain("Soft Skills");
        markdown.ShouldNotContain("Leadership");
    }

    [Fact]
    public void Emphasized_skills_render_bold()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldContain("**C#**");
    }

    [Fact]
    public void Includes_the_summary_text()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldContain("A backend engineer with distributed systems experience.");
    }

    [Fact]
    public void Omits_the_summary_heading_when_summary_is_null()
    {
        var document = RenderingTestData.Document() with { Summary = null };

        var markdown = _renderer.Render(document);

        markdown.ShouldNotContain("## Summary");
    }

    [Fact]
    public void Respects_a_custom_section_order()
    {
        var document = RenderingTestData.Document([SectionKind.Experience, SectionKind.Summary]);

        var markdown = _renderer.Render(document);

        markdown.IndexOf("## Experience", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("## Summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Renders_the_candidate_full_name_as_the_top_level_heading()
    {
        var markdown = _renderer.Render(RenderingTestData.Document());

        markdown.ShouldStartWith("# Jordan Rivera");
    }
}
