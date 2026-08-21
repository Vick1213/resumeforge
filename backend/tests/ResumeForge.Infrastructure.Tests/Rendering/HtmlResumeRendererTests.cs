using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Rendering;
using ResumeForge.Infrastructure.Tests.TestSupport;
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

    [Fact]
    public void Renders_hyperlinks_for_a_project_url_and_repo_url()
    {
        var project = TestData.Project(
            "prj:widget", "Widget Tool",
            bullets: [TestData.Bullet("prj:widget#0", "Shipped it.")],
            url: "https://widget.example.com", repoUrl: "https://github.com/jordan/widget");
        var document = RenderingTestData.Document() with { Projects = [project] };

        var html = _renderer.Render(document);

        html.ShouldContain("<a href=\"https://widget.example.com\">widget.example.com</a>");
        html.ShouldContain("<a href=\"https://github.com/jordan/widget\">github.com/jordan/widget</a>");
    }

    [Fact]
    public void Omits_a_project_with_no_bullets_and_no_tagline()
    {
        var bare = TestData.Project("prj:bare", "Bare Project", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1));
        var document = RenderingTestData.Document() with { Projects = [bare] };

        var html = _renderer.Render(document);

        html.ShouldNotContain("Bare Project");
        html.ShouldNotContain("<h2>Projects</h2>");
    }

    [Fact]
    public void Includes_a_project_with_a_tagline_but_no_bullets()
    {
        var taglineOnly = TestData.Project("prj:tagline", "Tagline Project", tagline: "A one-liner with no bullets.");
        var document = RenderingTestData.Document() with { Projects = [taglineOnly] };

        var html = _renderer.Render(document);

        html.ShouldContain("Tagline Project");
        html.ShouldContain("A one-liner with no bullets.");
        html.ShouldContain("<h2>Projects</h2>");
    }

    [Fact]
    public void The_header_block_is_center_aligned()
    {
        var html = _renderer.Render(RenderingTestData.Document());

        html.ShouldContain(".header { margin-bottom: 4px; text-align: center; }");
    }

    [Fact]
    public void Default_section_order_places_education_right_after_summary()
    {
        var education = TestData.Education(
            "edu:uw", "University of Washington", "B.S. Computer Science", new DateOnly(2014, 9, 1), new DateOnly(2018, 6, 1));
        var document = RenderingTestData.Document() with { Education = [education] };

        var html = _renderer.Render(document);

        var summaryIdx = html.IndexOf("Summary</h2>", StringComparison.Ordinal);
        var educationIdx = html.IndexOf("Education</h2>", StringComparison.Ordinal);
        var skillsIdx = html.IndexOf("Skills</h2>", StringComparison.Ordinal);

        summaryIdx.ShouldBeLessThan(educationIdx);
        educationIdx.ShouldBeLessThan(skillsIdx);
    }

    [Fact]
    public void A_skill_group_label_leads_its_own_line_rather_than_sitting_in_a_fixed_column()
    {
        // The old two-column layout reserved the width of the longest label on every row and
        // wrapped the skills into what was left, which is how a five-group block came to eat
        // a third of the page.
        var html = new HtmlResumeRenderer().Render(RenderingTestData.Document());

        html.ShouldContain("<span class=\"skill-label\">Languages:</span>");
        html.ShouldNotContain("<dt>");
    }

    [Fact]
    public void A_tagline_on_a_project_that_already_has_bullets_is_not_rendered()
    {
        var withBullets = TestData.Project(
            "prj:app", "Ledgerline",
            bullets: [TestData.Bullet("prj:app#0", "Cut cold-start time from 900ms to 120ms.")],
            tagline: "A double-entry ledger for small businesses.");

        var html = new HtmlResumeRenderer().Render(RenderingTestData.Document() with { Projects = [withBullets] });

        html.ShouldContain("Ledgerline");
        html.ShouldNotContain("A double-entry ledger for small businesses.");
    }

    [Fact]
    public void Education_highlights_join_the_metadata_line_instead_of_becoming_bullets()
    {
        var entry = TestData.Education(
            "edu:uw", "University of Washington", "B.S. Computer Science",
            new DateOnly(2014, 9, 1), new DateOnly(2018, 6, 1),
            highlights: ["Coursework: Distributed Systems"]);

        var html = new HtmlResumeRenderer().Render(
            RenderingTestData.Document() with { Education = [entry] });

        html.ShouldContain("<p class=\"location\">");
        html.ShouldContain("Coursework: Distributed Systems");
        html.ShouldNotContain("<li>Coursework: Distributed Systems</li>");
    }
}
