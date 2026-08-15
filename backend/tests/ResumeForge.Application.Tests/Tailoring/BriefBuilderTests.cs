using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Tests for <see cref="BriefBuilder"/> — the token-economics piece. The central assertion
/// is the documented ceiling: a realistic brief must stay under ~1800 estimated tokens.
/// </summary>
public sealed class BriefBuilderTests
{
    private readonly BriefBuilder _builder = new();

    [Theory]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("12345678", 2)]
    public void EstimateTokens_uses_a_chars_over_four_heuristic(string text, int expected)
    {
        _builder.EstimateTokens(text).ShouldBe(expected);
    }

    [Fact]
    public void A_realistic_brief_stays_under_the_1800_estimated_token_ceiling()
    {
        // A realistic base resume: 3 roles x 5 bullets, 2 projects x 4 bullets, 15 skills,
        // and 15 job requirements — representative of an actual tailoring run.
        var (baseResume, candidates) = BuildRealisticFixture(experienceEntries: 3, bulletsPerExperience: 5, projectEntries: 2, bulletsPerProject: 4, skillCount: 15);
        var analysis = BuildRequirements(15);
        var options = new TailorOptions();

        var brief = _builder.Build(analysis, candidates, baseResume, options);
        var estimated = _builder.EstimateTokens(brief);

        estimated.ShouldBeLessThan(1800);
    }

    [Fact]
    public void Brief_never_includes_full_bullet_text_beyond_the_truncation_cap()
    {
        var longText = new string('x', 500);
        var bullet = TestData.Bullet("exp:acme#0", longText);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet]);
        var baseResume = TestData.Document(experience: [entry]);

        var candidates = new CandidateSet
        {
            Experience = [new ScoredCandidate { EntityId = "exp:acme#0", Text = longText, Score = 1.0, MatchedRequirements = [] }],
            Projects = [],
            Skills = [],
        };

        var brief = _builder.Build(TestData.Analysis(), candidates, baseResume, new TailorOptions());

        brief.ShouldNotContain(longText);
        brief.Length.ShouldBeLessThan(longText.Length);
    }

    [Fact]
    public void Brief_respects_the_candidate_limit_per_section()
    {
        var bullets = Enumerable.Range(0, 20).Select(i => TestData.Bullet($"exp:acme#{i}", $"Bullet {i}")).ToList();
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: bullets);
        var baseResume = TestData.Document(experience: [entry]);

        var candidates = new CandidateSet
        {
            Experience = [.. bullets.Select(b => new ScoredCandidate { EntityId = b.Id, Text = b.Text, Score = 1.0, MatchedRequirements = [] })],
            Projects = [],
            Skills = [],
        };

        var brief = _builder.Build(TestData.Analysis(), candidates, baseResume, new TailorOptions { CandidateLimit = 5 });

        var experienceLines = brief.Split('\n').Where(l => l.StartsWith("exp:acme#", StringComparison.Ordinal)).ToList();
        experienceLines.Count.ShouldBe(5);
    }

    [Fact]
    public void Brief_includes_mandatory_flag_and_variant_count()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Cut latency significantly.", variants: ["v1", "v2"]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet]);
        var baseResume = TestData.Document(experience: [entry]);

        var candidates = new CandidateSet
        {
            Experience = [new ScoredCandidate { EntityId = "exp:acme#0", Text = bullet.Text, Score = 1.0, MatchedRequirements = [] }],
            Projects = [],
            Skills = [],
        };

        var analysis = TestData.Analysis(requirements:
            [TestData.Requirement("req:0", "Must know C#", RequirementKind.Skill, true)]);

        var brief = _builder.Build(analysis, candidates, baseResume, new TailorOptions());

        brief.ShouldContain("req:0|M|Must know C#");
        brief.ShouldContain("exp:acme#0|v2|Cut latency significantly.");
    }

    private static (ResumeDocument BaseResume, CandidateSet Candidates) BuildRealisticFixture(
        int experienceEntries, int bulletsPerExperience, int projectEntries, int bulletsPerProject, int skillCount)
    {
        var experience = new List<ExperienceEntry>();
        var experienceCandidates = new List<ScoredCandidate>();

        for (var e = 0; e < experienceEntries; e++)
        {
            var bullets = new List<Bullet>();
            for (var b = 0; b < bulletsPerExperience; b++)
            {
                var id = $"exp:company-{e}#{b}";
                var text = $"Delivered measurable impact on project {e}.{b} by improving reliability and performance across the stack.";
                bullets.Add(TestData.Bullet(id, text, variants: ["An alternate phrasing of the same accomplishment."]));
                experienceCandidates.Add(new ScoredCandidate { EntityId = id, Text = text, Score = 1.0, MatchedRequirements = [] });
            }

            experience.Add(TestData.Experience($"exp:company-{e}", "Engineer", $"Company {e}", new DateOnly(2020 + e, 1, 1), null, bullets: bullets));
        }

        var projects = new List<ProjectEntry>();
        var projectCandidates = new List<ScoredCandidate>();

        for (var p = 0; p < projectEntries; p++)
        {
            var bullets = new List<Bullet>();
            for (var b = 0; b < bulletsPerProject; b++)
            {
                var id = $"prj:project-{p}#{b}";
                var text = $"Built feature {p}.{b} of a side project used by a small community of users.";
                bullets.Add(TestData.Bullet(id, text));
                projectCandidates.Add(new ScoredCandidate { EntityId = id, Text = text, Score = 1.0, MatchedRequirements = [] });
            }

            projects.Add(TestData.Project($"prj:project-{p}", $"Project {p}", bullets: bullets));
        }

        var skills = Enumerable.Range(0, skillCount)
            .Select(i => TestData.Skill($"skl:tools#skill{i}", $"Skill {i}", $"skill{i}"))
            .ToList();
        var group = TestData.SkillGroup("skl:tools", "Tools", skills);
        var skillCandidates = skills.Select(s => new ScoredCandidate { EntityId = s.Id, Text = s.Name, Score = 1.0, MatchedRequirements = [] }).ToList();

        var baseResume = TestData.Document(experience: experience, projects: projects, skills: [group]);
        var candidates = new CandidateSet { Experience = experienceCandidates, Projects = projectCandidates, Skills = skillCandidates };

        return (baseResume, candidates);
    }

    private static JobAnalysis BuildRequirements(int count)
    {
        var requirements = Enumerable.Range(0, count)
            .Select(i => TestData.Requirement($"req:{i}", $"Requirement number {i} describing a qualification the role expects.", RequirementKind.Skill, i % 2 == 0))
            .ToList();

        return TestData.Analysis(requirements: requirements);
    }
}
