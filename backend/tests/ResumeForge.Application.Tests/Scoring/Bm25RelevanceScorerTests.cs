using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Scoring;

/// <summary>Tests for <see cref="Bm25RelevanceScorer"/>.</summary>
public sealed class Bm25RelevanceScorerTests
{
    private static Bm25RelevanceScorer NewScorer(FixedTimeProvider? time = null, ScoringOptions? options = null) =>
        new(time ?? new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), options ?? new ScoringOptions());

    [Fact]
    public void Bullet_matching_more_jd_skills_outranks_one_matching_fewer()
    {
        var strongBullet = TestData.Bullet(
            "exp:acme#0", "Built distributed systems in Kubernetes and AWS using Python and PostgreSQL",
            tags: ["kubernetes", "aws", "python", "postgresql"]);
        var weakBullet = TestData.Bullet(
            "exp:acme#1", "Wrote documentation for internal tools", tags: []);

        var entry = TestData.Experience(
            "exp:acme", "Engineer", "Acme", new DateOnly(2023, 1, 1), null, bullets: [strongBullet, weakBullet]);
        var doc = TestData.Document(experience: [entry]);

        var analysis = TestData.Analysis(requirements:
        [
            TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"]),
            TestData.Requirement("req:1", "AWS required", RequirementKind.Skill, true, ["aws"]),
            TestData.Requirement("req:2", "Python required", RequirementKind.Skill, true, ["python"]),
        ], keywords: ["kubernetes", "aws", "python", "postgresql"]);

        var result = NewScorer().Score(doc, analysis);

        var strong = result.Experience.Single(c => c.EntityId == "exp:acme#0");
        var weak = result.Experience.Single(c => c.EntityId == "exp:acme#1");
        strong.Score.ShouldBeGreaterThan(weak.Score);
    }

    [Fact]
    public void More_recent_role_scores_higher_than_an_older_role_with_identical_bullet()
    {
        const string text = "Shipped a new checkout flow using Kubernetes";
        var now = new DateOnly(2026, 1, 1);

        var recentEntry = TestData.Experience(
            "exp:recent", "Engineer", "Acme", now.AddYears(-1), null,
            bullets: [TestData.Bullet("exp:recent#0", text, tags: ["kubernetes"])]);

        var oldEntry = TestData.Experience(
            "exp:old", "Engineer", "OldCo", now.AddYears(-9), now.AddYears(-8),
            bullets: [TestData.Bullet("exp:old#0", text, tags: ["kubernetes"])]);

        var doc = TestData.Document(experience: [recentEntry, oldEntry]);
        var analysis = TestData.Analysis(
            requirements: [TestData.Requirement("req:0", "Kubernetes required", RequirementKind.Skill, true, ["kubernetes"])],
            keywords: ["kubernetes"]);

        var time = new FixedTimeProvider(now.ToDateTime(TimeOnly.MinValue));
        var result = NewScorer(time).Score(doc, analysis);

        var recent = result.Experience.Single(c => c.EntityId == "exp:recent#0");
        var old = result.Experience.Single(c => c.EntityId == "exp:old#0");
        recent.Score.ShouldBeGreaterThan(old.Score);
    }

    [Fact]
    public void Scores_stay_within_zero_and_one()
    {
        var entry = TestData.Experience(
            "exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null,
            bullets:
            [
                TestData.Bullet("exp:acme#0", "Kubernetes AWS Python PostgreSQL Kubernetes AWS", tags: ["kubernetes", "aws", "python", "postgresql"]),
                TestData.Bullet("exp:acme#1", "Nothing relevant here at all", tags: []),
            ]);

        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(
            requirements:
            [
                TestData.Requirement("req:0", "Kubernetes and AWS and Python and PostgreSQL required", RequirementKind.Skill, true,
                    ["kubernetes", "aws", "python", "postgresql"]),
            ],
            keywords: ["kubernetes", "aws", "python", "postgresql"]);

        var result = NewScorer().Score(doc, analysis);

        result.Experience.ShouldAllBe(c => c.Score >= 0.0 && c.Score <= 1.0);
    }

    [Fact]
    public void Excluded_entries_are_not_scored()
    {
        var entry = TestData.Experience(
            "exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null,
            bullets: [TestData.Bullet("exp:acme#0", "Kubernetes work", tags: ["kubernetes"])],
            included: false);

        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis(keywords: ["kubernetes"]);

        var result = NewScorer().Score(doc, analysis);
        result.Experience.ShouldBeEmpty();
    }

    [Fact]
    public void Skill_candidate_ranked_by_keyword_position_scores_higher_than_lower_ranked_skill()
    {
        var group = TestData.SkillGroup("skl:languages", "Languages",
        [
            TestData.Skill("skl:languages#csharp", "C#", "csharp"),
            TestData.Skill("skl:languages#python", "Python", "python"),
            TestData.Skill("skl:languages#ruby", "Ruby", "ruby"),
        ]);

        var doc = TestData.Document(skills: [group]);
        var analysis = TestData.Analysis(keywords: ["csharp", "python"]);

        var result = NewScorer().Score(doc, analysis);

        var csharp = result.Skills.Single(c => c.EntityId == "skl:languages#csharp");
        var python = result.Skills.Single(c => c.EntityId == "skl:languages#python");
        var ruby = result.Skills.Single(c => c.EntityId == "skl:languages#ruby");

        csharp.Score.ShouldBeGreaterThan(python.Score);
        python.Score.ShouldBeGreaterThan(ruby.Score);
        ruby.Score.ShouldBe(0.0);
    }

    [Fact]
    public void Empty_resume_and_analysis_produce_empty_candidate_sets_without_throwing()
    {
        var doc = TestData.Document();
        var analysis = TestData.Analysis();

        var result = NewScorer().Score(doc, analysis);

        result.Experience.ShouldBeEmpty();
        result.Projects.ShouldBeEmpty();
        result.Skills.ShouldBeEmpty();
    }

    [Fact]
    public void Candidates_are_sorted_descending_by_score_with_stable_id_tiebreak()
    {
        var entry = TestData.Experience(
            "exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null,
            bullets:
            [
                TestData.Bullet("exp:acme#1", "no match here", tags: []),
                TestData.Bullet("exp:acme#0", "also no match here", tags: []),
            ]);

        var doc = TestData.Document(experience: [entry]);
        var analysis = TestData.Analysis();

        var result = NewScorer().Score(doc, analysis);

        // Equal (zero) scores must tie-break by entity id, ascending, never by insertion order.
        result.Experience.Select(c => c.EntityId).ShouldBe(["exp:acme#0", "exp:acme#1"]);
    }

    [Fact]
    public void ScoringOptions_normalize_sums_to_one()
    {
        var options = new ScoringOptions { Bm25Weight = 2, SkillWeight = 1, RecencyWeight = 1 };
        var (bm25, skill, recency) = options.Normalize();

        (bm25 + skill + recency).ShouldBe(1.0, tolerance: 0.0000001);
        bm25.ShouldBe(0.5, tolerance: 0.0000001);
    }

    [Fact]
    public void ScoringOptions_normalize_falls_back_to_equal_split_when_sum_is_zero()
    {
        var options = new ScoringOptions { Bm25Weight = 0, SkillWeight = 0, RecencyWeight = 0 };
        var (bm25, skill, recency) = options.Normalize();

        bm25.ShouldBe(1.0 / 3, tolerance: 0.0000001);
        skill.ShouldBe(1.0 / 3, tolerance: 0.0000001);
        recency.ShouldBe(1.0 / 3, tolerance: 0.0000001);
    }
}
