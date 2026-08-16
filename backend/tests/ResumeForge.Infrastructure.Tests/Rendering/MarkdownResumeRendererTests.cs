using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Rendering;
using ResumeForge.Infrastructure.Tests.TestSupport;
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

    [Fact]
    public void Includes_a_projects_url_and_repo_url_when_present()
    {
        var project = TestData.Project(
            "prj:widget", "Widget Tool",
            bullets: [TestData.Bullet("prj:widget#0", "Shipped it.")],
            url: "https://widget.example.com", repoUrl: "https://github.com/jordan/widget");
        var document = RenderingTestData.Document() with { Projects = [project] };

        var markdown = _renderer.Render(document);

        markdown.ShouldContain("https://widget.example.com");
        markdown.ShouldContain("https://github.com/jordan/widget");
    }

    [Fact]
    public void Omits_a_project_with_no_bullets_and_no_tagline()
    {
        var bare = TestData.Project("prj:bare", "Bare Project", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1));
        var document = RenderingTestData.Document() with { Projects = [bare] };

        var markdown = _renderer.Render(document);

        markdown.ShouldNotContain("Bare Project");
        markdown.ShouldNotContain("## Projects");
    }

    [Fact]
    public void Includes_a_project_with_a_tagline_but_no_bullets()
    {
        var taglineOnly = TestData.Project("prj:tagline", "Tagline Project", tagline: "A one-liner with no bullets.");
        var document = RenderingTestData.Document() with { Projects = [taglineOnly] };

        var markdown = _renderer.Render(document);

        markdown.ShouldContain("Tagline Project");
        markdown.ShouldContain("A one-liner with no bullets.");
        markdown.ShouldContain("## Projects");
    }

    [Fact]
    public void Default_section_order_places_education_right_after_summary()
    {
        var education = TestData.Education(
            "edu:uw", "University of Washington", "B.S. Computer Science", new DateOnly(2014, 9, 1), new DateOnly(2018, 6, 1));
        var document = RenderingTestData.Document() with { Education = [education] };

        var markdown = _renderer.Render(document);

        var summaryIdx = markdown.IndexOf("## Summary", StringComparison.Ordinal);
        var educationIdx = markdown.IndexOf("## Education", StringComparison.Ordinal);
        var skillsIdx = markdown.IndexOf("## Skills", StringComparison.Ordinal);

        summaryIdx.ShouldBeLessThan(educationIdx);
        educationIdx.ShouldBeLessThan(skillsIdx);
    }
}
