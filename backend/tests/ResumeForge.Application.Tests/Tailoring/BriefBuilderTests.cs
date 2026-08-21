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
    public void The_ats_review_becomes_the_brief_s_stated_objective()
    {
        var (baseResume, candidates) = BuildRealisticFixture(1, 2, 1, 2, 4);
        var brief = _builder.Build(
            TestData.Posting(), BuildRequirements(2), candidates, baseResume, new TailorOptions(), Review());

        brief.ShouldContain("ATS-SCORE|54|81");
        brief.ShouldContain("ATS-GAPS");
        brief.ShouldContain("Kubernetes|critical|S|exp:acme#0|");
        brief.ShouldContain("ATS-RECRUITER-NOTES");
    }

    [Fact]
    public void A_gap_already_in_the_skills_list_is_marked_so_the_model_does_not_re_add_it_there()
    {
        var (baseResume, candidates) = BuildRealisticFixture(1, 2, 1, 2, 4);
        var brief = _builder.Build(
            TestData.Posting(), BuildRequirements(2), candidates, baseResume, new TailorOptions(), Review());

        // 'S' is the flag that says the parser is already satisfied and only a bullet closes
        // the gap; '-' says the term is absent from the document entirely.
        var gapLines = brief.Split('\n').Where(l => l.StartsWith("Kubernetes|", StringComparison.Ordinal)
            || l.StartsWith("Terraform|", StringComparison.Ordinal)).ToList();

        gapLines.ShouldContain(l => l.StartsWith("Kubernetes|critical|S|", StringComparison.Ordinal));
        gapLines.ShouldContain(l => l.StartsWith("Terraform|important|-|", StringComparison.Ordinal));
    }

    [Fact]
    public void A_brief_built_without_a_review_carries_no_ats_section_at_all()
    {
        // The review is advisory and may not have run (Minimal effort) or may have failed;
        // in either case the brief must be exactly what it was before the pass existed.
        var (baseResume, candidates) = BuildRealisticFixture(1, 2, 1, 2, 4);
        var analysis = BuildRequirements(2);
        var options = new TailorOptions();
        var posting = TestData.Posting();

        var withoutReview = _builder.Build(posting, analysis, candidates, baseResume, options, atsReview: null);

        withoutReview.ShouldNotContain("ATS-");
        withoutReview.ShouldBe(_builder.Build(posting, analysis, candidates, baseResume, options));
    }

    private static AtsReview Review() => new()
    {
        ScoreBefore = 54,
        ScoreAfter = 81,
        Verdict = "Strong platform work, but the posting's core infrastructure terms never appear in a bullet.",
        Gaps =
        [
            new AtsGap
            {
                Keyword = "Kubernetes",
                Importance = AtsGapImportance.Critical,
                SkillsOnly = true,
                Placement = "exp:acme#0",
                Angle = "The deploy-time work in this bullet ran on Kubernetes; name it alongside the 40% figure.",
            },
            new AtsGap
            {
                Keyword = "Terraform",
                Importance = AtsGapImportance.Important,
                SkillsOnly = false,
                Placement = null,
                Angle = "Nothing in the resume evidences it.",
            },
        ],
        RecruiterNotes = ["Two of the posting's core terms appear only in the skills list."],
    };

    [Fact]
    public void A_realistic_brief_stays_under_the_estimated_token_ceiling()
    {
        // A realistic base resume: 3 roles x 5 bullets, 2 projects x 4 bullets, 15 skills,
        // and 15 job requirements — representative of an actual tailoring run — against a
        // posting longer than the excerpt budget, so the excerpt is the binding constraint
        // rather than the fixture's length.
        var (baseResume, candidates) = BuildRealisticFixture(experienceEntries: 3, bulletsPerExperience: 5, projectEntries: 2, bulletsPerProject: 4, skillCount: 15);
        var analysis = BuildRequirements(15);
        var options = new TailorOptions();
        var posting = TestData.Posting(rawText: LongPostingText(options.PostingExcerptChars * 2));

        var brief = _builder.Build(posting, analysis, candidates, baseResume, options);
        var estimated = _builder.EstimateTokens(brief);

        // Raised from 1800 when the posting excerpt was added: the model was previously
        // judging alignment from extracted requirements alone, which is a lossy heuristic.
        // The ceiling is still asserted — and still bounded, since PostingExcerptChars caps
        // the new component — because "input grew for a reason" must not become "input grew
        // unnoticed". Output tokens, the half that dominates cost, are untouched.
        estimated.ShouldBeLessThan(3000);
    }

    [Fact]
    public void Brief_bounds_the_posting_excerpt_and_marks_where_it_cut()
    {
        var (baseResume, candidates) = BuildRealisticFixture(1, 1, 0, 0, 1);
        var options = new TailorOptions { PostingExcerptChars = 200 };
        var posting = TestData.Posting(rawText: LongPostingText(5000));

        var brief = _builder.Build(posting, BuildRequirements(1), candidates, baseResume, options);

        brief.ShouldContain("POSTING");
        brief.ShouldContain("(posting truncated)");
        brief.Length.ShouldBeLessThan(5000);
    }

    [Fact]
    public void Brief_omits_the_posting_entirely_when_the_excerpt_budget_is_zero()
    {
        var (baseResume, candidates) = BuildRealisticFixture(1, 1, 0, 0, 1);
        var posting = TestData.Posting(rawText: "We are hiring a backend engineer.");

        var brief = _builder.Build(
            posting, BuildRequirements(1), candidates, baseResume, new TailorOptions { PostingExcerptChars = 0 });

        brief.ShouldNotContain("POSTING");
        brief.ShouldNotContain("We are hiring");
    }

    [Fact]
    public void Brief_carries_the_job_header_so_the_model_knows_what_it_is_tailoring_to()
    {
        var (baseResume, candidates) = BuildRealisticFixture(1, 1, 0, 0, 1);
        var posting = TestData.Posting(title: "Senior Backend Engineer", company: "Acme | Corp");

        var brief = _builder.Build(posting, BuildRequirements(1), candidates, baseResume, new TailorOptions());

        // The company's own pipe is neutralized, or it would shift every later field.
        brief.ShouldContain("JOB|Senior Backend Engineer|Acme / Corp||");
    }

    [Fact]
    public void Brief_lists_project_taglines_only_at_full_effort()
    {
        var project = TestData.Project(
            "prj:tinyorm", "TinyORM", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1),
            tagline: "A minimal ORM for small services.");
        var baseResume = TestData.Document(projects: [project]);
        var candidates = new CandidateSet { Experience = [], Projects = [], Skills = [] };

        var atFull = _builder.Build(
            TestData.Posting(), BuildRequirements(1), candidates, baseResume, new TailorOptions { Effort = ModelEffort.Full });
        var atMaximum = _builder.Build(
            TestData.Posting(), BuildRequirements(1), candidates, baseResume, new TailorOptions { Effort = ModelEffort.Maximum });

        // Without this the model cannot emit setTagline at all: every other section keys off
        // bullet ids, so the project's own id never appears in the brief.
        atFull.ShouldContain("TAGLINES");
        atFull.ShouldContain("prj:tinyorm|A minimal ORM for small services.");
        atMaximum.ShouldNotContain("TAGLINES");
    }

    private static string LongPostingText(int approximateChars) =>
        string.Join(
            '\n',
            Enumerable.Range(0, Math.Max(1, approximateChars / 60))
                .Select(i => $"Responsibility {i}: own a service end to end and mentor peers."));

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

        var brief = _builder.Build(TestData.Posting(), TestData.Analysis(), candidates, baseResume, new TailorOptions());

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

        var brief = _builder.Build(TestData.Posting(), TestData.Analysis(), candidates, baseResume, new TailorOptions { CandidateLimit = 5 });

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

        var brief = _builder.Build(TestData.Posting(), analysis, candidates, baseResume, new TailorOptions());

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
