using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Structural tests for <see cref="TailoringGraphFactory"/>: the declared node set and
/// dependency edges must match CONTRACTS.md §7's diagram, plus the <c>enforce-page-budget</c>
/// node CONTRACTS.md §6 ("Page budget") adds between <c>execute-commands</c> and
/// <c>render</c> — so the executor can run the three <c>score-*</c> nodes — and the three
/// <c>verify-*</c>/<c>execute-commands</c> nodes — concurrently. Also covers the deterministic
/// headline override (CONTRACTS.md §2 "Tailored headline") the <c>execute-commands</c> node
/// applies via <see cref="TailoringGraphFactory.ApplyTailoredHeadline"/>.
/// </summary>
public sealed class TailoringGraphFactoryTests
{
    private static ResumeForge.Application.Graph.Graph BuildGraph()
    {
        var factory = new TailoringGraphFactory(
            Substitute.For<IJobRepository>(),
            Substitute.For<IJobAnalyzer>(),
            Substitute.For<IKnowledgeBaseReader>(),
            Substitute.For<IResumeBuilder>(),
            Substitute.For<IRelevanceScorer>(),
            Substitute.For<IBriefBuilder>(),
            Substitute.For<ILanguageModel>(),
            Substitute.For<ICommandValidator>(),
            Substitute.For<IFabricationGuard>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICoverageAnalyzer>(),
            Substitute.For<IResumeRenderer>(),
            Substitute.For<IPageBudgetEnforcer>(),
            new TailorOptions());

        return factory.Create(new TailoringRequest { JobId = "job-1" });
    }

    private static IReadOnlyList<string> DependsOn(ResumeForge.Application.Graph.Graph graph, string name) =>
        graph.Nodes.Single(n => n.Name == name).DependsOn;

    [Fact]
    public void Graph_declares_exactly_the_fifteen_documented_nodes()
    {
        var graph = BuildGraph();

        graph.Nodes.Select(n => n.Name).ShouldBe(
        [
            "fetch-jd", "load-kb", "analyze-jd", "build-base",
            "score-experience", "score-projects", "score-skills",
            "build-brief", "propose-commands", "validate-commands",
            "verify-fabrication", "verify-coverage", "execute-commands",
            "enforce-page-budget", "render",
        ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_three_score_nodes_have_no_edges_between_each_other()
    {
        var graph = BuildGraph();

        var scoreNodeNames = new HashSet<string> { "score-experience", "score-projects", "score-skills" };

        foreach (var name in scoreNodeNames)
        {
            DependsOn(graph, name).ShouldNotContain(other => scoreNodeNames.Contains(other) && other != name);
        }
    }

    [Fact]
    public void Build_brief_depends_on_all_three_score_nodes_and_on_the_posting_itself()
    {
        var graph = BuildGraph();

        // fetch-jd is declared even though every score node already reaches it transitively:
        // the brief reads the posting out of the context directly (for the JOB header and the
        // POSTING excerpt), and a node that reads a result must declare the edge that produced
        // it rather than rely on someone else's happening to schedule it first.
        DependsOn(graph, "build-brief").ShouldBe(
            ["fetch-jd", "score-experience", "score-projects", "score-skills"], ignoreOrder: true);
    }

    [Fact]
    public void Verify_fabrication_and_verify_coverage_have_no_edge_between_them()
    {
        var graph = BuildGraph();

        DependsOn(graph, "verify-fabrication").ShouldNotContain("verify-coverage");
        DependsOn(graph, "verify-coverage").ShouldNotContain("verify-fabrication");
    }

    [Fact]
    public void Verify_fabrication_verify_coverage_and_execute_commands_are_independent_siblings_of_validate_commands()
    {
        var graph = BuildGraph();

        DependsOn(graph, "verify-fabrication").ShouldBe(["validate-commands"]);
        DependsOn(graph, "verify-coverage").ShouldBe(["validate-commands"]);
        DependsOn(graph, "execute-commands").ShouldBe(["validate-commands"]);
    }

    [Fact]
    public void Render_depends_on_both_verification_nodes_and_the_page_budget_node()
    {
        var graph = BuildGraph();

        DependsOn(graph, "render").ShouldBe(["verify-fabrication", "verify-coverage", "enforce-page-budget"], ignoreOrder: true);
    }

    [Fact]
    public void Enforce_page_budget_depends_on_execute_commands_and_the_three_score_nodes()
    {
        var graph = BuildGraph();

        DependsOn(graph, "enforce-page-budget").ShouldBe(
            ["execute-commands", "score-experience", "score-projects", "score-skills"], ignoreOrder: true);
    }

    [Fact]
    public void Fetch_jd_and_load_kb_are_root_nodes_with_no_dependencies()
    {
        var graph = BuildGraph();

        DependsOn(graph, "fetch-jd").ShouldBeEmpty();
        DependsOn(graph, "load-kb").ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_jd_depends_only_on_fetch_jd()
    {
        var graph = BuildGraph();
        DependsOn(graph, "analyze-jd").ShouldBe(["fetch-jd"]);
    }

    [Fact]
    public void Build_base_depends_only_on_load_kb()
    {
        var graph = BuildGraph();
        DependsOn(graph, "build-base").ShouldBe(["load-kb"]);
    }

    [Fact]
    public void Propose_commands_is_the_only_node_depending_directly_on_build_brief()
    {
        var graph = BuildGraph();

        var dependents = graph.Nodes.Where(n => n.DependsOn.Contains("build-brief")).Select(n => n.Name).ToList();
        dependents.ShouldBe(["propose-commands"]);
    }

    [Fact]
    public void Fetch_jd_and_load_kb_are_marked_critical()
    {
        var graph = BuildGraph();

        graph.Nodes.Single(n => n.Name == "fetch-jd").Critical.ShouldBeTrue();
        graph.Nodes.Single(n => n.Name == "load-kb").Critical.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_merges_the_requests_max_pages_override_into_the_options_the_page_budget_enforcer_receives()
    {
        // Regression: the `options = tailorOptions with { MaxRewrites = ..., Effort = ... }`
        // merge in Create copied Effort and MaxRewrites from the request but forgot
        // MaxPages, so enforce-page-budget always saw the DI-configured TailorOptions
        // default (2 here) regardless of what TailoringRequest.MaxPages asked for. This
        // runs the real graph through the real GraphExecutor — every other port stubbed to
        // a minimal success — and inspects the TailorOptions the page-budget node actually
        // hands IPageBudgetEnforcer.EnforceAsync.
        var document = TestData.Document();
        var posting = TestData.Posting();
        var analysis = TestData.Analysis();
        var candidates = new CandidateSet { Experience = [], Projects = [], Skills = [] };

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(analysis);

        var knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
        knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeBaseSnapshot { Items = [], Basics = TestData.Basics(), Diagnostics = [] });

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>()).Returns(candidates);

        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(Arg.Any<JobPosting>(), Arg.Any<JobAnalysis>(), Arg.Any<CandidateSet>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns("brief");

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel.CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse<TailorCommandParseResultList>
            {
                Value = new TailorCommandParseResultList([]),
                Usage = TokenUsage.Empty,
                FromCache = false,
            });

        var commandValidator = Substitute.For<ICommandValidator>();
        commandValidator
            .Validate(Arg.Any<TailorCommandParseResultList>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns(new CommandValidationResult { Accepted = [], Rejected = [] });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.Execute(Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<TailorCommand>>())
            .Returns(new CommandExecutionResult { Document = document, Diff = [] });

        var coverageAnalyzer = Substitute.For<ICoverageAnalyzer>();
        coverageAnalyzer.Analyze(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CoverageReport { Score = 0, Requirements = [] });

        var resumeRenderer = Substitute.For<IResumeRenderer>();
        resumeRenderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/html", FileName = "resume.html" });

        TailorOptions? optionsSeenByEnforcer = null;
        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(),
                Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(),
                Arg.Do<TailorOptions>(o => optionsSeenByEnforcer = o),
                Arg.Any<CancellationToken>())
            .Returns(new PageBudgetResult { Document = document, Diff = [], PageCount = 1, FitsBudget = true });

        // The DI-configured default is MaxPages = 2 (TailorOptions()'s own default), so if
        // the merge bug regresses, optionsSeenByEnforcer.MaxPages comes back 2, not 1.
        var factory = new TailoringGraphFactory(
            jobRepository, jobAnalyzer, knowledgeBaseReader, resumeBuilder, relevanceScorer, briefBuilder,
            languageModel, commandValidator, Substitute.For<IFabricationGuard>(), commandExecutor,
            coverageAnalyzer, resumeRenderer, pageBudgetEnforcer, new TailorOptions());

        var graph = factory.Create(new TailoringRequest { JobId = posting.Id, MaxPages = 1 });

        var executor = new GraphExecutor(TimeProvider.System, new GraphOptions(), NullLogger<GraphExecutor>.Instance);
        var result = await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        optionsSeenByEnforcer.ShouldNotBeNull();
        optionsSeenByEnforcer.MaxPages.ShouldBe(1);
    }

    [Fact]
    public async Task Create_passes_an_explicit_null_max_pages_override_through_to_the_page_budget_enforcer()
    {
        // Mirrors the test above for the other half of the contract (CONTRACTS.md §6): an
        // explicit null must disable trimming, not silently fall back to the DI default.
        var document = TestData.Document();
        var posting = TestData.Posting();
        var analysis = TestData.Analysis();
        var candidates = new CandidateSet { Experience = [], Projects = [], Skills = [] };

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(analysis);

        var knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
        knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeBaseSnapshot { Items = [], Basics = TestData.Basics(), Diagnostics = [] });

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>()).Returns(candidates);

        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(Arg.Any<JobPosting>(), Arg.Any<JobAnalysis>(), Arg.Any<CandidateSet>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns("brief");

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel.CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse<TailorCommandParseResultList>
            {
                Value = new TailorCommandParseResultList([]),
                Usage = TokenUsage.Empty,
                FromCache = false,
            });

        var commandValidator = Substitute.For<ICommandValidator>();
        commandValidator
            .Validate(Arg.Any<TailorCommandParseResultList>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns(new CommandValidationResult { Accepted = [], Rejected = [] });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.Execute(Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<TailorCommand>>())
            .Returns(new CommandExecutionResult { Document = document, Diff = [] });

        var coverageAnalyzer = Substitute.For<ICoverageAnalyzer>();
        coverageAnalyzer.Analyze(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CoverageReport { Score = 0, Requirements = [] });

        var resumeRenderer = Substitute.For<IResumeRenderer>();
        resumeRenderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/html", FileName = "resume.html" });

        TailorOptions? optionsSeenByEnforcer = null;
        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(),
                Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(),
                Arg.Do<TailorOptions>(o => optionsSeenByEnforcer = o),
                Arg.Any<CancellationToken>())
            .Returns(new PageBudgetResult { Document = document, Diff = [], PageCount = 1, FitsBudget = true });

        var factory = new TailoringGraphFactory(
            jobRepository, jobAnalyzer, knowledgeBaseReader, resumeBuilder, relevanceScorer, briefBuilder,
            languageModel, commandValidator, Substitute.For<IFabricationGuard>(), commandExecutor,
            coverageAnalyzer, resumeRenderer, pageBudgetEnforcer, new TailorOptions());

        var graph = factory.Create(new TailoringRequest { JobId = posting.Id, MaxPages = null });

        var executor = new GraphExecutor(TimeProvider.System, new GraphOptions(), NullLogger<GraphExecutor>.Instance);
        var result = await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        optionsSeenByEnforcer.ShouldNotBeNull();
        optionsSeenByEnforcer.MaxPages.ShouldBeNull();
    }

    [Fact]
    public void Apply_tailored_headline_uses_the_normalized_job_title_when_one_was_determined_at_ingest()
    {
        var document = WithHeadline("Full-Stack Software Engineer");

        var result = TailoringGraphFactory.ApplyTailoredHeadline(document, "Senior Backend Engineer");

        result.Basics.Headline.ShouldBe("Senior Backend Engineer");
    }

    [Fact]
    public void Apply_tailored_headline_keeps_the_profile_headline_when_the_job_title_is_null()
    {
        var document = WithHeadline("Full-Stack Software Engineer");

        var result = TailoringGraphFactory.ApplyTailoredHeadline(document, jobTitle: null);

        result.Basics.Headline.ShouldBe("Full-Stack Software Engineer");
    }

    [Fact]
    public void Apply_tailored_headline_keeps_the_profile_headline_when_the_job_title_is_whitespace()
    {
        var document = WithHeadline("Full-Stack Software Engineer");

        var result = TailoringGraphFactory.ApplyTailoredHeadline(document, "   ");

        result.Basics.Headline.ShouldBe("Full-Stack Software Engineer");
    }

    [Fact]
    public void Apply_tailored_headline_trims_and_collapses_internal_whitespace_runs_in_the_job_title()
    {
        var document = WithHeadline("Full-Stack Software Engineer");

        var result = TailoringGraphFactory.ApplyTailoredHeadline(document, "  Senior   Backend\tEngineer  ");

        result.Basics.Headline.ShouldBe("Senior Backend Engineer");
    }

    private static ResumeDocument WithHeadline(string headline) =>
        TestData.Document() with { Basics = TestData.Document().Basics with { Headline = headline } };

    [Fact]
    public void Apply_forced_inclusion_pins_an_entry_the_model_excluded()
    {
        var document = TestData.Document(
            projects: [TestData.Project("prj:side", "Side Project", included: false)]);

        var result = TailoringGraphFactory.ApplyForcedInclusion(document, pinnedEntryIds: ["prj:side"], excludedEntryIds: null);

        result.Projects.Single(p => p.Id == "prj:side").Included.ShouldBeTrue();
    }

    [Fact]
    public void Apply_forced_inclusion_excludes_an_entry_the_model_included()
    {
        var document = TestData.Document(
            experience: [TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null, included: true)]);

        var result = TailoringGraphFactory.ApplyForcedInclusion(document, pinnedEntryIds: null, excludedEntryIds: ["exp:acme"]);

        result.Experience.Single(e => e.Id == "exp:acme").Included.ShouldBeFalse();
    }

    [Fact]
    public void Apply_forced_inclusion_applies_a_pin_and_an_exclude_together_on_different_entries()
    {
        var document = TestData.Document(
            experience: [TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null, included: true)],
            projects: [TestData.Project("prj:side", "Side Project", included: false)]);

        var result = TailoringGraphFactory.ApplyForcedInclusion(
            document, pinnedEntryIds: ["prj:side"], excludedEntryIds: ["exp:acme"]);

        result.Projects.Single(p => p.Id == "prj:side").Included.ShouldBeTrue();
        result.Experience.Single(e => e.Id == "exp:acme").Included.ShouldBeFalse();
    }

    [Fact]
    public void Apply_forced_inclusion_is_a_no_op_for_null_lists()
    {
        var document = TestData.Document(
            experience: [TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null, included: true)]);

        var result = TailoringGraphFactory.ApplyForcedInclusion(document, pinnedEntryIds: null, excludedEntryIds: null);

        result.ShouldBe(document);
    }

    [Fact]
    public void Apply_forced_inclusion_is_a_no_op_for_empty_lists()
    {
        var document = TestData.Document(
            experience: [TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null, included: true)]);

        var result = TailoringGraphFactory.ApplyForcedInclusion(document, pinnedEntryIds: [], excludedEntryIds: []);

        result.ShouldBe(document);
    }

    [Fact]
    public async Task Create_merges_the_requests_pinned_and_excluded_entry_ids_into_the_options_the_page_budget_enforcer_receives()
    {
        // Mirrors Create_merges_the_requests_max_pages_override_into_the_options_the_page_budget_enforcer_receives
        // above: PinnedEntryIds/ExcludedEntryIds must reach the same TailorOptions instance
        // the page-budget node hands IPageBudgetEnforcer.EnforceAsync, or a pin/exclude
        // silently no-ops the same way the MaxPages bug this mirrors once did.
        var document = TestData.Document();
        var posting = TestData.Posting();
        var analysis = TestData.Analysis();
        var candidates = new CandidateSet { Experience = [], Projects = [], Skills = [] };

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(analysis);

        var knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
        knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeBaseSnapshot { Items = [], Basics = TestData.Basics(), Diagnostics = [] });

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>()).Returns(candidates);

        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(Arg.Any<JobPosting>(), Arg.Any<JobAnalysis>(), Arg.Any<CandidateSet>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns("brief");

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel.CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse<TailorCommandParseResultList>
            {
                Value = new TailorCommandParseResultList([]),
                Usage = TokenUsage.Empty,
                FromCache = false,
            });

        var commandValidator = Substitute.For<ICommandValidator>();
        commandValidator
            .Validate(Arg.Any<TailorCommandParseResultList>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns(new CommandValidationResult { Accepted = [], Rejected = [] });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.Execute(Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<TailorCommand>>())
            .Returns(new CommandExecutionResult { Document = document, Diff = [] });

        var coverageAnalyzer = Substitute.For<ICoverageAnalyzer>();
        coverageAnalyzer.Analyze(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CoverageReport { Score = 0, Requirements = [] });

        var resumeRenderer = Substitute.For<IResumeRenderer>();
        resumeRenderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/html", FileName = "resume.html" });

        TailorOptions? optionsSeenByEnforcer = null;
        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(),
                Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(),
                Arg.Do<TailorOptions>(o => optionsSeenByEnforcer = o),
                Arg.Any<CancellationToken>())
            .Returns(new PageBudgetResult { Document = document, Diff = [], PageCount = 1, FitsBudget = true });

        var factory = new TailoringGraphFactory(
            jobRepository, jobAnalyzer, knowledgeBaseReader, resumeBuilder, relevanceScorer, briefBuilder,
            languageModel, commandValidator, Substitute.For<IFabricationGuard>(), commandExecutor,
            coverageAnalyzer, resumeRenderer, pageBudgetEnforcer, new TailorOptions());

        var graph = factory.Create(new TailoringRequest
        {
            JobId = posting.Id,
            PinnedEntryIds = ["prj:pinned"],
            ExcludedEntryIds = ["exp:excluded"],
        });

        var executor = new GraphExecutor(TimeProvider.System, new GraphOptions(), NullLogger<GraphExecutor>.Instance);
        var result = await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        optionsSeenByEnforcer.ShouldNotBeNull();
        optionsSeenByEnforcer.PinnedEntryIds.ShouldBe(["prj:pinned"]);
        optionsSeenByEnforcer.ExcludedEntryIds.ShouldBe(["exp:excluded"]);
    }

    [Fact]
    public async Task Create_normalizes_null_pinned_and_excluded_entry_ids_to_empty_lists_in_the_options()
    {
        // Mirrors the null-MaxPages test above: an omitted list must not surface as a null
        // reference downstream (PageBudgetEnforcer and ApplyForcedInclusion both assume a
        // non-null, possibly-empty TailorOptions.PinnedEntryIds/ExcludedEntryIds).
        var document = TestData.Document();
        var posting = TestData.Posting();
        var analysis = TestData.Analysis();
        var candidates = new CandidateSet { Experience = [], Projects = [], Skills = [] };

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(analysis);

        var knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
        knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeBaseSnapshot { Items = [], Basics = TestData.Basics(), Diagnostics = [] });

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>()).Returns(candidates);

        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(Arg.Any<JobPosting>(), Arg.Any<JobAnalysis>(), Arg.Any<CandidateSet>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns("brief");

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel.CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse<TailorCommandParseResultList>
            {
                Value = new TailorCommandParseResultList([]),
                Usage = TokenUsage.Empty,
                FromCache = false,
            });

        var commandValidator = Substitute.For<ICommandValidator>();
        commandValidator
            .Validate(Arg.Any<TailorCommandParseResultList>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns(new CommandValidationResult { Accepted = [], Rejected = [] });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.Execute(Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<TailorCommand>>())
            .Returns(new CommandExecutionResult { Document = document, Diff = [] });

        var coverageAnalyzer = Substitute.For<ICoverageAnalyzer>();
        coverageAnalyzer.Analyze(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CoverageReport { Score = 0, Requirements = [] });

        var resumeRenderer = Substitute.For<IResumeRenderer>();
        resumeRenderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/html", FileName = "resume.html" });

        TailorOptions? optionsSeenByEnforcer = null;
        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(),
                Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(),
                Arg.Do<TailorOptions>(o => optionsSeenByEnforcer = o),
                Arg.Any<CancellationToken>())
            .Returns(new PageBudgetResult { Document = document, Diff = [], PageCount = 1, FitsBudget = true });

        var factory = new TailoringGraphFactory(
            jobRepository, jobAnalyzer, knowledgeBaseReader, resumeBuilder, relevanceScorer, briefBuilder,
            languageModel, commandValidator, Substitute.For<IFabricationGuard>(), commandExecutor,
            coverageAnalyzer, resumeRenderer, pageBudgetEnforcer, new TailorOptions());

        var graph = factory.Create(new TailoringRequest { JobId = posting.Id });

        var executor = new GraphExecutor(TimeProvider.System, new GraphOptions(), NullLogger<GraphExecutor>.Instance);
        var result = await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        optionsSeenByEnforcer.ShouldNotBeNull();
        optionsSeenByEnforcer.PinnedEntryIds.ShouldBeEmpty();
        optionsSeenByEnforcer.ExcludedEntryIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Build_brief_omits_candidates_belonging_to_a_force_excluded_entry()
    {
        // CONTRACTS.md §6 "Forced inclusion": a force-excluded entry's bullets must not
        // reach IBriefBuilder.Build's CandidateSet, since ExecuteCommands will override
        // whatever the model decides about it anyway.
        var document = TestData.Document();
        var posting = TestData.Posting();
        var analysis = TestData.Analysis();

        var experienceCandidates = new List<ScoredCandidate>
        {
            new() { EntityId = "exp:keep#0", Text = "x", Score = 0.9, MatchedRequirements = [] },
            new() { EntityId = "exp:drop#0", Text = "x", Score = 0.8, MatchedRequirements = [] },
        };
        var projectCandidates = new List<ScoredCandidate>
        {
            new() { EntityId = "prj:keep#0", Text = "x", Score = 0.7, MatchedRequirements = [] },
        };
        var candidates = new CandidateSet { Experience = experienceCandidates, Projects = projectCandidates, Skills = [] };

        var jobRepository = Substitute.For<IJobRepository>();
        jobRepository.GetAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(posting);

        var jobAnalyzer = Substitute.For<IJobAnalyzer>();
        jobAnalyzer.Analyze(Arg.Any<JobPosting>()).Returns(analysis);

        var knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
        knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeBaseSnapshot { Items = [], Basics = TestData.Basics(), Diagnostics = [] });

        var resumeBuilder = Substitute.For<IResumeBuilder>();
        resumeBuilder.Build(Arg.Any<KnowledgeBaseSnapshot>(), Arg.Any<string?>(), Arg.Any<string>()).Returns(document);

        var relevanceScorer = Substitute.For<IRelevanceScorer>();
        relevanceScorer.Score(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>()).Returns(candidates);

        CandidateSet? candidateSetSeenByBriefBuilder = null;
        var briefBuilder = Substitute.For<IBriefBuilder>();
        briefBuilder.Build(
                Arg.Any<JobPosting>(),
                Arg.Any<JobAnalysis>(),
                Arg.Do<CandidateSet>(c => candidateSetSeenByBriefBuilder = c),
                Arg.Any<ResumeDocument>(),
                Arg.Any<TailorOptions>())
            .Returns("brief");

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel.CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse<TailorCommandParseResultList>
            {
                Value = new TailorCommandParseResultList([]),
                Usage = TokenUsage.Empty,
                FromCache = false,
            });

        var commandValidator = Substitute.For<ICommandValidator>();
        commandValidator
            .Validate(Arg.Any<TailorCommandParseResultList>(), Arg.Any<ResumeDocument>(), Arg.Any<TailorOptions>())
            .Returns(new CommandValidationResult { Accepted = [], Rejected = [] });

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.Execute(Arg.Any<ResumeDocument>(), Arg.Any<IReadOnlyList<TailorCommand>>())
            .Returns(new CommandExecutionResult { Document = document, Diff = [] });

        var coverageAnalyzer = Substitute.For<ICoverageAnalyzer>();
        coverageAnalyzer.Analyze(Arg.Any<ResumeDocument>(), Arg.Any<JobAnalysis>())
            .Returns(new CoverageReport { Score = 0, Requirements = [] });

        var resumeRenderer = Substitute.For<IResumeRenderer>();
        resumeRenderer.RenderAsync(Arg.Any<ResumeDocument>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns(new RenderedDocument { Content = [], ContentType = "text/html", FileName = "resume.html" });

        var pageBudgetEnforcer = Substitute.For<IPageBudgetEnforcer>();
        pageBudgetEnforcer
            .EnforceAsync(
                Arg.Any<ResumeDocument>(),
                Arg.Any<IReadOnlyList<ResumeDiffEntry>>(),
                Arg.Any<CandidateSet>(),
                Arg.Any<TailorOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new PageBudgetResult { Document = document, Diff = [], PageCount = 1, FitsBudget = true });

        var factory = new TailoringGraphFactory(
            jobRepository, jobAnalyzer, knowledgeBaseReader, resumeBuilder, relevanceScorer, briefBuilder,
            languageModel, commandValidator, Substitute.For<IFabricationGuard>(), commandExecutor,
            coverageAnalyzer, resumeRenderer, pageBudgetEnforcer, new TailorOptions());

        var graph = factory.Create(new TailoringRequest { JobId = posting.Id, ExcludedEntryIds = ["exp:drop"] });

        var executor = new GraphExecutor(TimeProvider.System, new GraphOptions(), NullLogger<GraphExecutor>.Instance);
        var result = await executor.RunAsync(graph, Substitute.For<IServiceProvider>(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        candidateSetSeenByBriefBuilder.ShouldNotBeNull();
        candidateSetSeenByBriefBuilder.Experience.Select(c => c.EntityId).ShouldBe(["exp:keep#0"]);
        candidateSetSeenByBriefBuilder.Projects.Select(c => c.EntityId).ShouldBe(["prj:keep#0"]);
    }
}
