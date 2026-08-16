using ResumeForge.Application.Analysis;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Analysis;

/// <summary>
/// Tests for <see cref="PastedJobTitleExtractor"/> (CONTRACTS.md §9), the conservative
/// title extractor used on the pasted-<c>rawText</c> branch of <c>POST /api/jobs</c>.
/// </summary>
public sealed class PastedJobTitleExtractorTests
{
    [Fact]
    public void Extracts_a_plain_title_from_the_first_line()
    {
        var rawText =
            "Senior Software Engineer\n" +
            "Acme Corp is looking for a talented engineer to join our growing platform team.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBe("Senior Software Engineer");
    }

    [Fact]
    public void Captures_the_remainder_of_an_explicitly_labeled_title_line()
    {
        var rawText =
            "Job Title: Staff Software Engineer, Platform\n" +
            "Acme Corp builds developer tools used by thousands of teams.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBe("Staff Software Engineer, Platform");
    }

    [Fact]
    public void Does_not_extract_a_long_company_blurb_sentence_as_a_title()
    {
        // Real-world case that motivates the conservatism: the opening line reads like a
        // title-shaped sentence but is company boilerplate — it ends with '.' and exceeds
        // the word cap, so it must not become the resume headline.
        const string opener =
            "JT4, LLC provides engineering and technical support to multiple western test " +
            "ranges, working on cutting-edge projects.";
        opener.EndsWith('.').ShouldBeTrue();
        opener.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBeGreaterThan(12);

        var rawText = opener + "\nWe are an equal opportunity employer committed to a diverse workplace.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBeNull();
    }

    [Fact]
    public void Rejects_a_role_keyword_line_that_exceeds_the_length_bound()
    {
        var words = new[]
        {
            "Senior", "Principal", "Distinguished", "Fellow", "Enterprise", "Cloud",
            "Platform", "Reliability", "Infrastructure", "Engineer",
        };
        var line = string.Join(' ', words);
        line.Length.ShouldBeGreaterThan(80);
        words.Length.ShouldBeLessThanOrEqualTo(12);

        PastedJobTitleExtractor.Extract(line).ShouldBeNull();
    }

    [Fact]
    public void Rejects_a_role_keyword_line_that_ends_with_a_period()
    {
        PastedJobTitleExtractor.Extract("We need a great Software Engineer.").ShouldBeNull();
    }

    [Fact]
    public void Finds_the_title_on_a_later_line_when_earlier_lines_are_prose()
    {
        var rawText =
            "About the opportunity.\n" +
            "We are growing fast and need great people.\n" +
            "Backend Engineer\n" +
            "You will build and maintain our core services.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBe("Backend Engineer");
    }

    [Fact]
    public void Returns_null_when_nothing_in_the_first_lines_qualifies()
    {
        var rawText =
            "About Us.\n" +
            "We build great things for great customers.\n" +
            "Join our journey today.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBeNull();
    }

    [Fact]
    public void Normalizes_internal_whitespace_runs_before_evaluating_a_line()
    {
        var rawText = "  Senior   Software    Engineer  \nAcme is hiring across the platform org.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBe("Senior Software Engineer");
    }

    [Fact]
    public void Normalizes_whitespace_in_the_remainder_of_a_labeled_title_line()
    {
        var rawText = "Title:    Senior   DevOps   Engineer   \nAcme runs a large cloud footprint.";

        PastedJobTitleExtractor.Extract(rawText).ShouldBe("Senior DevOps Engineer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_blank_or_missing_raw_text(string? rawText)
    {
        PastedJobTitleExtractor.Extract(rawText).ShouldBeNull();
    }
}
