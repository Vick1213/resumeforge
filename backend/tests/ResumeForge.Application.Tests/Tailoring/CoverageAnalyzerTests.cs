using ResumeForge.Application.Analysis;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="CoverageAnalyzer"/>.</summary>
public sealed class CoverageAnalyzerTests
{
    private readonly CoverageAnalyzer _analyzer = new();

    [Fact]
    public void Requirement_evidenced_by_an_included_bullet_tag_is_covered()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Built with Kubernetes.", tags: ["kubernetes"]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet]);
        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"])]);

        var report = _analyzer.Analyze(doc, analysis);

        var coverage = report.Requirements.Single();
        coverage.Covered.ShouldBeTrue();
        coverage.EvidenceIds.ShouldBe(["exp:acme#0"]);
    }

    [Fact]
    public void Requirement_with_no_matching_evidence_is_not_covered()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Wrote documentation.", tags: []);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet]);
        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"])]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Requirements.Single().Covered.ShouldBeFalse();
    }

    [Fact]
    public void Evidence_from_an_excluded_entry_does_not_count_as_coverage()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Built with Kubernetes.", tags: ["kubernetes"]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet], included: false);
        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"])]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Requirements.Single().Covered.ShouldBeFalse();
        report.Score.ShouldBe(0.0);
    }

    [Fact]
    public void Skill_evidence_counts_toward_coverage()
    {
        var skill = TestData.Skill("skl:languages#csharp", "C#", "csharp");
        var group = TestData.SkillGroup("skl:languages", "Languages", [skill]);
        var doc = TestData.Document(skills: [group]);
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "C# required", RequirementKind.Skill, true, ["csharp"])]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Requirements.Single().Covered.ShouldBeTrue();
        report.Requirements.Single().EvidenceIds.ShouldBe(["skl:languages#csharp"]);
    }

    [Fact]
    public void Score_is_the_fraction_of_mandatory_requirements_covered()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Built with Kubernetes.", tags: ["kubernetes"]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet]);
        var doc = TestData.Document(experience: [entry]);

        var analysis = TestData.Analysis(requirements:
        [
            TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"]),
            TestData.Requirement("req:1", "AWS required", RequirementKind.Skill, true, ["aws"]),
            TestData.Requirement("req:2", "Docker preferred", RequirementKind.Skill, false, ["docker"]),
        ]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Score.ShouldBe(0.5, tolerance: 0.0001);
    }

    [Fact]
    public void Score_is_one_when_there_are_no_mandatory_requirements()
    {
        var doc = TestData.Document();
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Docker preferred", RequirementKind.Skill, false, ["docker"])]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Score.ShouldBe(1.0);
    }

    [Fact]
    public void Score_is_one_when_there_are_no_requirements_at_all()
    {
        var doc = TestData.Document();
        var analysis = TestData.Analysis();

        var report = _analyzer.Analyze(doc, analysis);

        report.Score.ShouldBe(1.0);
        report.Requirements.ShouldBeEmpty();
    }

    [Fact]
    public void Evidence_ids_are_sorted_deterministically()
    {
        var bulletB = TestData.Bullet("exp:acme#1", "Built with Kubernetes and AWS.", tags: ["kubernetes"]);
        var bulletA = TestData.Bullet("exp:acme#0", "Also used Kubernetes.", tags: ["kubernetes"]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bulletB, bulletA]);
        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"])]);

        var report = _analyzer.Analyze(doc, analysis);

        report.Requirements.Single().EvidenceIds.ShouldBe(["exp:acme#0", "exp:acme#1"]);
    }
}
