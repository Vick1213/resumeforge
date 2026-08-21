using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Tests for <see cref="PageBudgetEnforcer"/> (CONTRACTS.md §6 "Page budget"). Uses a
/// stub <see cref="IResumeRenderer"/> whose reported page count is a deterministic
/// function of the document's included entries, rather than a real QuestPDF render, so
/// these assert the enforcer's *behavior* (cut order, the floor, diff entries, loop
/// termination) and stay unaffected by any future typography/density retuning of the real
/// renderer.
/// </summary>
public sealed class PageBudgetEnforcerTests
{
    private const string ExpTop = "exp:top";

    [Fact]
    public async Task Default_max_pages_is_two_and_a_large_knowledge_base_is_cut_to_fit()
    {
        // Fourteen included entries at three-per-page starts at 5 pages; the default
        // budget of 2 needs the count down to 6 or fewer. Certifications and projects
        // (2 + 6 = 8 entries) alone are enough to get there without ever touching
        // experience, since every project/cert scores no higher than the lowest-scoring
        // experience entry in this fixture.
        var document = BuildDocument(
            experienceCount: 6, // exp:top plus five more, all scored below it
            projectCount: 6,
            certificationCount: 2);
        var candidates = BuildCandidates(document);

        var renderer = new StubRenderer(entriesPerPage: 3);
        var enforcer = new PageBudgetEnforcer(renderer);

        var result = await enforcer.EnforceAsync(document, [], candidates, new TailorOptions(), CancellationToken.None);

        result.FitsBudget.ShouldBeTrue();
        result.PageCount.ShouldBeLessThanOrEqualTo(2);
        result.Document.Certifications.ShouldAllBe(c => !c.Included);
        result.Document.Projects.ShouldAllBe(p => !p.Included);
        result.Document.Experience.ShouldAllBe(e => e.Included);

        var cuts = result.Diff.Where(d => d.Kind == DiffKind.Excluded).ToList();
        cuts.Count.ShouldBe(8);
        cuts.ShouldAllBe(d => d.Rationale != null && d.Rationale.Contains("budget", StringComparison.OrdinalIgnoreCase));
        cuts.ShouldAllBe(d => d.Rationale!.Contains("2-page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cut_order_drops_a_certification_and_a_project_before_an_experience_entry_at_equal_relevance()
    {
        var document = TestData.Document(
            experience:
            [
                TestData.Experience(ExpTop, "Staff Engineer", "Acme", new DateOnly(2022, 1, 1), null,
                    bullets: [TestData.Bullet($"{ExpTop}#0", "Led the platform team.")]),
                TestData.Experience("exp:tied", "Engineer", "OldCo", new DateOnly(2019, 1, 1), new DateOnly(2021, 1, 1),
                    bullets: [TestData.Bullet("exp:tied#0", "Shipped a feature.")]),
            ],
            projects: [TestData.Project("prj:tied", "Side Project", bullets: [TestData.Bullet("prj:tied#0", "Built a tool.")])],
            certifications: [TestData.Certification("cert:tied", "Some Cert")]);

        // exp:tied and prj:tied are scored identically (0.0) — the same score every
        // certification carries implicitly, having no relevance score of its own. Only
        // section kind can break this tie.
        var candidates = new CandidateSet
        {
            Experience =
            [
                new ScoredCandidate { EntityId = $"{ExpTop}#0", Text = "x", Score = 0.9, MatchedRequirements = [] },
                new ScoredCandidate { EntityId = "exp:tied#0", Text = "x", Score = 0.0, MatchedRequirements = [] },
            ],
            Projects = [new ScoredCandidate { EntityId = "prj:tied#0", Text = "x", Score = 0.0, MatchedRequirements = [] }],
            Skills = [],
        };

        // One included entry per page: 4 included -> 4 pages; budget of 2 needs exactly
        // two cuts.
        var renderer = new StubRenderer(entriesPerPage: 1);
        var enforcer = new PageBudgetEnforcer(renderer);

        var result = await enforcer.EnforceAsync(document, [], candidates, new TailorOptions { MaxPages = 2 }, CancellationToken.None);

        result.Document.Certifications.Single().Included.ShouldBeFalse();
        result.Document.Projects.Single().Included.ShouldBeFalse();
        result.Document.Experience.Single(e => e.Id == "exp:tied").Included.ShouldBeTrue();
        result.Document.Experience.Single(e => e.Id == ExpTop).Included.ShouldBeTrue();
        result.FitsBudget.ShouldBeTrue();
    }

    [Fact]
    public async Task The_floor_holds_basics_and_every_experience_entry_survive_an_impossible_budget()
    {
        var document = BuildDocument(experienceCount: 3, projectCount: 2, certificationCount: 2);
        var candidates = BuildCandidates(document);

        // No matter how much gets cut, the stub always reports more pages than the
        // budget allows — an impossibly small target, forcing the loop all the way to
        // the floor. Experience is never cuttable, so the whole employment history
        // survives even a budget that can never be met.
        var renderer = new StubRenderer(_ => 99);
        var enforcer = new PageBudgetEnforcer(renderer);

        var result = await enforcer.EnforceAsync(document, [], candidates, new TailorOptions { MaxPages = 1 }, CancellationToken.None);

        result.FitsBudget.ShouldBeFalse();
        result.PageCount.ShouldBe(99);
        result.Document.Basics.ShouldBe(document.Basics);
        result.Document.Certifications.ShouldAllBe(c => !c.Included);
        result.Document.Projects.ShouldAllBe(p => !p.Included);
        result.Document.Experience.ShouldAllBe(e => e.Included);
    }

    [Fact]
    public async Task Max_pages_null_disables_trimming_entirely()
    {
        var document = BuildDocument(experienceCount: 10, projectCount: 10, certificationCount: 5);
        var candidates = BuildCandidates(document);

        var renderer = new StubRenderer(_ => 40); // wildly over any plausible budget
        var enforcer = new PageBudgetEnforcer(renderer);

        var result = await enforcer.EnforceAsync(document, [], candidates, new TailorOptions { MaxPages = null }, CancellationToken.None);

        result.FitsBudget.ShouldBeTrue();
        result.PageCount.ShouldBe(40);
        result.Document.Certifications.ShouldAllBe(c => c.Included);
        result.Document.Projects.ShouldAllBe(p => p.Included);
        result.Document.Experience.ShouldAllBe(e => e.Included);
        result.Diff.ShouldBeEmpty();
        renderer.RenderCount.ShouldBe(1); // only the initial page-count render, never a cutting pass
    }

    [Fact]
    public async Task The_loop_terminates_on_a_pathological_input_via_the_explicit_pass_cap()
    {
        // A hundred cuttable certifications, and a stub that reports "over budget"
        // forever no matter what is cut — the natural per-entry bound alone would still
        // allow up to 100 render passes. MaxPageBudgetPasses caps it far below that.
        var document = TestData.Document(
            experience: [TestData.Experience(ExpTop, "Staff Engineer", "Acme", new DateOnly(2022, 1, 1), null,
                bullets: [TestData.Bullet($"{ExpTop}#0", "Led the platform team.")])],
            certifications: [.. Enumerable.Range(0, 100).Select(i => TestData.Certification($"cert:c{i}", $"Cert {i}"))]);

        var candidates = new CandidateSet
        {
            Experience = [new ScoredCandidate { EntityId = $"{ExpTop}#0", Text = "x", Score = 0.9, MatchedRequirements = [] }],
            Projects = [],
            Skills = [],
        };

        var renderer = new StubRenderer(_ => 999); // never fits, regardless of what gets cut
        var enforcer = new PageBudgetEnforcer(renderer);

        var options = new TailorOptions { MaxPages = 1, MaxPageBudgetPasses = 5 };

        var result = await enforcer.EnforceAsync(document, [], candidates, options, CancellationToken.None);

        result.FitsBudget.ShouldBeFalse();
        result.Diff.Count(d => d.Kind == DiffKind.Excluded).ShouldBe(5);
        result.Document.Certifications.Count(c => !c.Included).ShouldBe(5);
        renderer.RenderCount.ShouldBe(6); // one initial render, plus exactly one per capped pass
    }

    [Fact]
    public async Task A_pinned_low_relevance_project_survives_while_unpinned_higher_relevance_projects_are_cut()
    {
        // CONTRACTS.md §6 "Forced inclusion": a pin extends the never-cut floor regardless
        // of relevance. "prj:pinned" scores at the very bottom of the ranking (lower than
        // every other project here), yet must survive a tight budget that cuts the
        // higher-scoring, unpinned projects around it.
        var document = TestData.Document(
            experience:
            [
                TestData.Experience(ExpTop, "Staff Engineer", "Acme", new DateOnly(2023, 1, 1), null,
                    bullets: [TestData.Bullet($"{ExpTop}#0", "Led the platform team.")]),
            ],
            projects:
            [
                TestData.Project("prj:pinned", "Pinned Project", bullets: [TestData.Bullet("prj:pinned#0", "Low relevance work.")]),
                .. Enumerable.Range(0, 10).Select(i =>
                    TestData.Project($"prj:p{i}", $"Project {i}", bullets: [TestData.Bullet($"prj:p{i}#0", $"Higher relevance work {i}.")])),
            ]);

        var candidates = new CandidateSet
        {
            Experience = [new ScoredCandidate { EntityId = $"{ExpTop}#0", Text = "x", Score = 0.9, MatchedRequirements = [] }],
            Projects =
            [
                new ScoredCandidate { EntityId = "prj:pinned#0", Text = "x", Score = 0.01, MatchedRequirements = [] },
                .. Enumerable.Range(0, 10).Select(i =>
                    new ScoredCandidate { EntityId = $"prj:p{i}#0", Text = "x", Score = 0.5 - (i * 0.01), MatchedRequirements = [] }),
            ],
            Skills = [],
        };

        // One included entry per page and a budget of 2: only the never-cut floor (ExpTop)
        // plus the pin can ever satisfy that, so every unpinned project must be cut.
        var renderer = new StubRenderer(entriesPerPage: 1);
        var enforcer = new PageBudgetEnforcer(renderer);

        var options = new TailorOptions { MaxPages = 2, PinnedEntryIds = ["prj:pinned"] };
        var result = await enforcer.EnforceAsync(document, [], candidates, options, CancellationToken.None);

        result.FitsBudget.ShouldBeTrue();
        result.Document.Projects.Single(p => p.Id == "prj:pinned").Included.ShouldBeTrue();
        result.Document.Projects.Where(p => p.Id != "prj:pinned").ShouldAllBe(p => !p.Included);
        result.Document.Experience.Single(e => e.Id == ExpTop).Included.ShouldBeTrue();
    }

    [Fact]
    public async Task Pins_that_cannot_fit_the_budget_leave_fits_budget_false_with_every_pinned_entry_still_included()
    {
        // Semantics rule 2: if pins alone exceed the budget, existing floor semantics apply
        // — stop, FitsBudget = false, rather than cutting a pinned entry to make it fit.
        var document = TestData.Document(
            experience:
            [
                TestData.Experience(ExpTop, "Staff Engineer", "Acme", new DateOnly(2023, 1, 1), null,
                    bullets: [TestData.Bullet($"{ExpTop}#0", "Led the platform team.")]),
            ],
            projects:
            [
                TestData.Project("prj:pinned-1", "Pinned One", bullets: [TestData.Bullet("prj:pinned-1#0", "Work one.")]),
                TestData.Project("prj:pinned-2", "Pinned Two", bullets: [TestData.Bullet("prj:pinned-2#0", "Work two.")]),
            ],
            certifications: [TestData.Certification("cert:unpinned", "Some Cert")]);

        var candidates = new CandidateSet
        {
            Experience = [new ScoredCandidate { EntityId = $"{ExpTop}#0", Text = "x", Score = 0.9, MatchedRequirements = [] }],
            Projects =
            [
                new ScoredCandidate { EntityId = "prj:pinned-1#0", Text = "x", Score = 0.5, MatchedRequirements = [] },
                new ScoredCandidate { EntityId = "prj:pinned-2#0", Text = "x", Score = 0.4, MatchedRequirements = [] },
            ],
            Skills = [],
        };

        // Always reports over budget, no matter what gets cut — forces the loop all the way
        // down to the (pinned) floor.
        var renderer = new StubRenderer(_ => 99);
        var enforcer = new PageBudgetEnforcer(renderer);

        var options = new TailorOptions { MaxPages = 1, PinnedEntryIds = ["prj:pinned-1", "prj:pinned-2"] };
        var result = await enforcer.EnforceAsync(document, [], candidates, options, CancellationToken.None);

        result.FitsBudget.ShouldBeFalse();
        result.Document.Projects.Single(p => p.Id == "prj:pinned-1").Included.ShouldBeTrue();
        result.Document.Projects.Single(p => p.Id == "prj:pinned-2").Included.ShouldBeTrue();
        result.Document.Certifications.Single(c => c.Id == "cert:unpinned").Included.ShouldBeFalse();
        result.Document.Experience.Single(e => e.Id == ExpTop).Included.ShouldBeTrue();
    }

    private static ResumeDocument BuildDocument(int experienceCount, int projectCount, int certificationCount)
    {
        var experience = new List<ExperienceEntry>
        {
            TestData.Experience(ExpTop, "Staff Engineer", "Acme", new DateOnly(2023, 1, 1), null,
                bullets: [TestData.Bullet($"{ExpTop}#0", "Led the platform team.")]),
        };

        for (var i = 0; i < experienceCount - 1; i++)
        {
            experience.Add(TestData.Experience(
                $"exp:e{i}", "Engineer", $"Org{i}", new DateOnly(2015 + i, 1, 1), new DateOnly(2016 + i, 1, 1),
                bullets: [TestData.Bullet($"exp:e{i}#0", $"Did work {i}.")]));
        }

        var projects = new List<ProjectEntry>();
        for (var i = 0; i < projectCount; i++)
        {
            projects.Add(TestData.Project($"prj:p{i}", $"Project {i}", bullets: [TestData.Bullet($"prj:p{i}#0", $"Built thing {i}.")]));
        }

        var certifications = new List<CertificationEntry>();
        for (var i = 0; i < certificationCount; i++)
        {
            certifications.Add(TestData.Certification($"cert:c{i}", $"Cert {i}"));
        }

        return TestData.Document(experience: experience, projects: projects, certifications: certifications);
    }

    /// <summary>
    /// Scores <see cref="ExpTop"/> highest (so it is always the floor); every other
    /// experience and project bullet scores lower, in a fixed descending order, so cut
    /// order is deterministic in tests that don't specifically probe ties.
    /// </summary>
    private static CandidateSet BuildCandidates(ResumeDocument document)
    {
        var experience = new List<ScoredCandidate>
        {
            new() { EntityId = $"{ExpTop}#0", Text = "x", Score = 0.9, MatchedRequirements = [] },
        };

        var otherExperience = document.Experience.Where(e => e.Id != ExpTop).ToList();
        for (var i = 0; i < otherExperience.Count; i++)
        {
            var bullet = otherExperience[i].Bullets[0];
            experience.Add(new ScoredCandidate { EntityId = bullet.Id, Text = "x", Score = 0.5 - (i * 0.01), MatchedRequirements = [] });
        }

        var projects = new List<ScoredCandidate>();
        for (var i = 0; i < document.Projects.Count; i++)
        {
            var bullet = document.Projects[i].Bullets[0];
            projects.Add(new ScoredCandidate { EntityId = bullet.Id, Text = "x", Score = 0.05 - (i * 0.001), MatchedRequirements = [] });
        }

        return new CandidateSet { Experience = experience, Projects = projects, Skills = [] };
    }

    /// <summary>A no-network <see cref="IResumeRenderer"/> whose page count is a pure function of the document.</summary>
    private sealed class StubRenderer : IResumeRenderer
    {
        private readonly Func<ResumeDocument, int> _pageCounter;

        public StubRenderer(Func<ResumeDocument, int> pageCounter) => _pageCounter = pageCounter;

        public StubRenderer(int entriesPerPage)
            : this(doc => Math.Max(1, (int)Math.Ceiling(CountIncluded(doc) / (double)entriesPerPage)))
        {
        }

        public int RenderCount { get; private set; }

        public Task<RenderedDocument> RenderAsync(ResumeDocument doc, RenderFormat format, CancellationToken ct)
        {
            RenderCount++;
            return Task.FromResult(new RenderedDocument
            {
                Content = [],
                ContentType = "application/pdf",
                FileName = "resume.pdf",
                PageCount = _pageCounter(doc),
            });
        }

        private static int CountIncluded(ResumeDocument doc) =>
            doc.Certifications.Count(c => c.Included) + doc.Projects.Count(p => p.Included) + doc.Experience.Count(e => e.Included);
    }
}
