using System.Text.Json;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Analysis;

/// <summary>Tests for <see cref="JobAnalyzer"/>.</summary>
public sealed class JobAnalyzerTests
{
    private const string SampleJd = """
        Senior Backend Engineer

        About Us
        We are a fast-growing company that loves Python and building great things.

        Requirements
        - 5+ years of experience with C# and distributed systems
        - Bachelor's degree in Computer Science or related field
        - Proficiency in Python and PostgreSQL

        Responsibilities
        - Design and build scalable APIs
        - Collaborate with cross-functional teams

        Nice to have
        - Experience with Kubernetes and AWS
        - Familiarity with Snowflake

        Benefits
        - Health insurance and 401k
        """;

    private static JobAnalyzer NewAnalyzer() => new(FakeSkillTaxonomy.CreateDefault());

    [Fact]
    public void Analyze_is_deterministic_across_repeated_calls()
    {
        var analyzer = NewAnalyzer();
        var posting = TestData.Posting(rawText: SampleJd, title: "Senior Backend Engineer");

        var first = analyzer.Analyze(posting);
        var second = analyzer.Analyze(posting);

        JsonSerializer.Serialize(first).ShouldBe(JsonSerializer.Serialize(second));
    }

    [Fact]
    public void Mandatory_requirement_is_classified_mandatory()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var yearsReq = analysis.Requirements.Single(r => r.Text.Contains("5+ years"));
        yearsReq.IsMandatory.ShouldBeTrue();
    }

    [Fact]
    public void Requirement_under_preferred_heading_is_optional_regardless_of_wording()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var preferredReq = analysis.Requirements.Single(r => r.Text.Contains("Kubernetes"));
        preferredReq.IsMandatory.ShouldBeFalse();
    }

    [Fact]
    public void Optional_cue_phrase_marks_requirement_optional_even_outside_preferred_heading()
    {
        const string jd = """
            Requirements
            - Experience with Rust is a plus
            """;

        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: jd));

        analysis.Requirements.Single().IsMandatory.ShouldBeFalse();
    }

    [Fact]
    public void About_us_and_benefits_sections_are_excluded_as_noise()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        analysis.Requirements.ShouldNotContain(r => r.Text.Contains("fast-growing"));
        analysis.Requirements.ShouldNotContain(r => r.Text.Contains("401k"));
        analysis.Requirements.ShouldNotContain(r => r.Text.Contains("Health insurance"));
    }

    [Fact]
    public void Keywords_only_reflect_non_noise_sections()
    {
        // "Python" is mentioned once in the noise "About Us" section and once in
        // Requirements; only the Requirements-section mention should count.
        const string jd = """
            About Us
            We love Python here.

            Requirements
            - Proficiency in Python
            """;

        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: jd));

        analysis.Keywords.ShouldContain("python");
        var pythonRequirement = analysis.Requirements.Single(r => r.Skills.Contains("python"));
        pythonRequirement.Text.ShouldContain("Proficiency");
    }

    [Fact]
    public void Education_requirement_is_classified_by_degree_cue()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var eduReq = analysis.Requirements.Single(r => r.Text.Contains("degree"));
        eduReq.Kind.ShouldBe(RequirementKind.Education);
    }

    [Fact]
    public void Experience_requirement_is_classified_by_years_cue()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var expReq = analysis.Requirements.Single(r => r.Text.Contains("5+ years"));
        expReq.Kind.ShouldBe(RequirementKind.Experience);
    }

    [Fact]
    public void Skill_requirement_is_classified_under_requirements_heading_with_no_other_cue()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var skillReq = analysis.Requirements.Single(r => r.Text.Contains("Proficiency in Python"));
        skillReq.Kind.ShouldBe(RequirementKind.Skill);
    }

    [Fact]
    public void Responsibility_is_classified_under_responsibilities_heading()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        var respReq = analysis.Requirements.Single(r => r.Text.Contains("cross-functional"));
        respReq.Kind.ShouldBe(RequirementKind.Responsibility);
    }

    [Theory]
    [InlineData("Senior Backend Engineer", SeniorityLevel.Senior)]
    [InlineData("Staff Software Engineer", SeniorityLevel.Staff)]
    [InlineData("Principal Engineer", SeniorityLevel.Principal)]
    [InlineData("Junior Developer", SeniorityLevel.Junior)]
    [InlineData("Software Engineering Intern", SeniorityLevel.Intern)]
    [InlineData("Backend Engineer", SeniorityLevel.Unknown)]
    public void Seniority_is_detected_from_title(string title, SeniorityLevel expected)
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: "Requirements\n- Build things", title: title));
        analysis.Seniority.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Requirements\n- 5+ years of experience required", SeniorityLevel.Senior)]
    [InlineData("Requirements\n- 9+ years of experience required", SeniorityLevel.Staff)]
    [InlineData("Requirements\n- 14+ years of experience required", SeniorityLevel.Principal)]
    [InlineData("Requirements\n- 1 year of experience required", SeniorityLevel.Junior)]
    public void Seniority_is_detected_from_years_of_experience_phrasing(string jd, SeniorityLevel expected)
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: jd));
        analysis.Seniority.ShouldBe(expected);
    }

    [Fact]
    public void Seniority_takes_the_higher_of_title_and_years_signal()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(
            rawText: "Requirements\n- 12+ years of experience required", title: "Junior Engineer"));

        analysis.Seniority.ShouldBe(SeniorityLevel.Principal);
    }

    [Fact]
    public void Requirement_ids_are_sequential()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));

        for (var i = 0; i < analysis.Requirements.Count; i++)
        {
            analysis.Requirements[i].Id.ShouldBe($"req:{i}");
        }
    }

    [Fact]
    public void Weight_is_higher_for_mandatory_first_position_requirement_than_optional_last_position()
    {
        const string jd = """
            Requirements
            - Mandatory requirement one

            Nice to have
            - Optional requirement two
            """;

        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: jd));

        var mandatory = analysis.Requirements.First(r => r.Text.Contains("Mandatory"));
        var optional = analysis.Requirements.First(r => r.Text.Contains("Optional"));

        mandatory.Weight.ShouldBeGreaterThan(optional.Weight);
        mandatory.Weight.ShouldBe(1.0, tolerance: 0.001);
    }

    [Fact]
    public void All_weights_are_within_0_and_1()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));
        analysis.Requirements.ShouldAllBe(r => r.Weight >= 0.0 && r.Weight <= 1.0);
    }

    [Fact]
    public void Keywords_are_capped_at_40_and_ties_break_alphabetically()
    {
        var taxonomy = new FakeSkillTaxonomy();
        var lines = new List<string> { "Requirements" };
        var expectedCanonical = new List<string>();

        for (var i = 0; i < 50; i++)
        {
            var canonical = $"skill{i:D2}";
            taxonomy.Add(canonical, "tools", canonical);
            lines.Add($"- Experience with {canonical}");
            expectedCanonical.Add(canonical);
        }

        var analyzer = new JobAnalyzer(taxonomy);
        var analysis = analyzer.Analyze(TestData.Posting(rawText: string.Join('\n', lines)));

        analysis.Keywords.Count.ShouldBe(40);
        var expectedTop40 = expectedCanonical.OrderBy(s => s, StringComparer.Ordinal).Take(40).ToList();
        analysis.Keywords.ShouldBe(expectedTop40);
    }

    [Fact]
    public void Unrecognized_skill_like_terms_in_requirements_become_missing_skills()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));
        analysis.MissingSkills.ShouldContain("snowflake");
    }

    [Fact]
    public void Matched_skills_are_a_subset_of_keywords_known_to_the_taxonomy()
    {
        var analysis = NewAnalyzer().Analyze(TestData.Posting(rawText: SampleJd));
        analysis.MatchedSkills.ShouldNotBeEmpty();
        analysis.MatchedSkills.ShouldBeSubsetOf(analysis.Keywords);
    }
}
