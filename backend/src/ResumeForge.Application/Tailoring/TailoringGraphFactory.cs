using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;
using TailoringGraph = ResumeForge.Application.Graph.Graph;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Declares the tailoring pipeline exactly as drawn in CONTRACTS.md §7: the three
/// <c>score-*</c> nodes depend only on <c>analyze-jd</c> and <c>build-base</c> and have no
/// edges between each other, so the executor runs them concurrently; <c>build-brief</c>
/// depends on all three. <c>verify-fabrication</c>, <c>verify-coverage</c>, and
/// <c>execute-commands</c> are likewise independent siblings depending only on
/// <c>validate-commands</c>. <c>propose-commands</c> is the only node that calls
/// <see cref="ILanguageModel"/>.
/// </summary>
public sealed class TailoringGraphFactory(
    IJobRepository jobRepository,
    IJobAnalyzer jobAnalyzer,
    IKnowledgeBaseReader knowledgeBaseReader,
    IResumeBuilder resumeBuilder,
    IRelevanceScorer relevanceScorer,
    IBriefBuilder briefBuilder,
    ILanguageModel languageModel,
    ICommandValidator commandValidator,
    IFabricationGuard fabricationGuard,
    ICommandExecutor commandExecutor,
    ICoverageAnalyzer coverageAnalyzer,
    IResumeRenderer resumeRenderer,
    IPageBudgetEnforcer pageBudgetEnforcer,
    TailorOptions tailorOptions) : ITailoringGraphFactory
{
    /// <summary>Node name: loads the persisted job posting for the run.</summary>
    public const string FetchJd = "fetch-jd";

    /// <summary>Node name: deterministic job description analysis.</summary>
    public const string AnalyzeJd = "analyze-jd";

    /// <summary>Node name: reads the knowledge base.</summary>
    public const string LoadKb = "load-kb";

    /// <summary>Node name: builds the base resume from the knowledge base.</summary>
    public const string BuildBase = "build-base";

    /// <summary>Node name: scores experience bullets.</summary>
    public const string ScoreExperience = "score-experience";

    /// <summary>Node name: scores project bullets.</summary>
    public const string ScoreProjects = "score-projects";

    /// <summary>Node name: scores skills.</summary>
    public const string ScoreSkills = "score-skills";

    /// <summary>Node name: builds the compact model brief.</summary>
    public const string BuildBrief = "build-brief";

    /// <summary>Node name: the only node that calls <see cref="ILanguageModel"/>.</summary>
    public const string ProposeCommands = "propose-commands";

    /// <summary>Node name: validates the proposed commands.</summary>
    public const string ValidateCommands = "validate-commands";

    /// <summary>Node name: re-checks accepted rewrites for fabrication.</summary>
    public const string VerifyFabrication = "verify-fabrication";

    /// <summary>Node name: computes requirement coverage of the tailored document.</summary>
    public const string VerifyCoverage = "verify-coverage";

    /// <summary>Node name: applies accepted commands to the base resume.</summary>
    public const string ExecuteCommands = "execute-commands";

    /// <summary>
    /// Node name: deterministically trims the executed document to
    /// <see cref="TailorOptions.MaxPages"/> (CONTRACTS.md §6 "Page budget").
    /// </summary>
    public const string EnforcePageBudget = "enforce-page-budget";

    /// <summary>Node name: renders the tailored document.</summary>
    public const string Render = "render";

    private const string SystemPrompt =
        "You tailor a resume to a job by emitting a JSON array of commands only. You never " +
        "write resume prose except through the rewrite command, and only when no existing " +
        "variant fits. Prefer selectVariant over rewrite. Reference only the requirement and " +
        "candidate ids given to you. Do not invent metrics or facts.";

    /// <inheritdoc />
    public TailoringGraph Create(TailoringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = tailorOptions with
        {
            MaxRewrites = request.Effort.ResolveMaxRewrites(request.MaxRewrites),
            Effort = request.Effort,
            MaxPages = request.MaxPages,
            PinnedEntryIds = request.PinnedEntryIds ?? [],
            ExcludedEntryIds = request.ExcludedEntryIds ?? [],
        };

        return new GraphBuilder()
            .AddNode(FetchJd, async (_, ct) =>
            {
                var posting = await jobRepository.GetAsync(request.JobId, ct).ConfigureAwait(false);
                return (object?)(posting ?? throw new InvalidOperationException($"Job posting '{request.JobId}' was not found."));
            })
            .Critical()

            .AddNode(LoadKb, async (_, ct) =>
            {
                var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
                return (object?)snapshot;
            })
            .Critical()

            .AddNode(AnalyzeJd, (ctx, _) =>
            {
                var posting = ctx.Get<JobPosting>(FetchJd);
                return Task.FromResult<object?>(jobAnalyzer.Analyze(posting));
            })
            .DependsOn(FetchJd)

            .AddNode(BuildBase, (ctx, _) =>
            {
                var snapshot = ctx.Get<KnowledgeBaseSnapshot>(LoadKb);
                var doc = resumeBuilder.Build(snapshot, request.BaseResumeId);
                return Task.FromResult<object?>(doc);
            })
            .DependsOn(LoadKb)

            .AddNode(ScoreExperience, (ctx, _) => Task.FromResult<object?>(ScoreSection(ctx, cs => cs.Experience)))
            .DependsOn(AnalyzeJd, BuildBase)

            .AddNode(ScoreProjects, (ctx, _) => Task.FromResult<object?>(ScoreSection(ctx, cs => cs.Projects)))
            .DependsOn(AnalyzeJd, BuildBase)

            .AddNode(ScoreSkills, (ctx, _) => Task.FromResult<object?>(ScoreSection(ctx, cs => cs.Skills)))
            .DependsOn(AnalyzeJd, BuildBase)

            .AddNode(BuildBrief, (ctx, _) =>
            {
                var analysis = ctx.Get<JobAnalysis>(AnalyzeJd);
                var baseDoc = ctx.Get<ResumeDocument>(BuildBase);

                // Force-excluded entries (CONTRACTS.md §6 "Forced inclusion") are dropped
                // from the model's candidate pool here rather than left for it to propose
                // excluding on its own: ExecuteCommands would override that decision anyway,
                // so offering the candidate at all just spends the model's limited command
                // budget on a foregone conclusion. Scoring (score-experience/score-projects,
                // upstream of this node) still runs over the whole base document, since
                // PageBudgetEnforcer's relevance ranking needs every entry's score regardless
                // of forced status.
                var candidates = new CandidateSet
                {
                    Experience = ExcludeForcedEntries(ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreExperience), options.ExcludedEntryIds),
                    Projects = ExcludeForcedEntries(ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreProjects), options.ExcludedEntryIds),
                    Skills = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreSkills),
                };

                var brief = briefBuilder.Build(analysis, candidates, baseDoc, options);
                return Task.FromResult<object?>(brief);
            })
            .DependsOn(ScoreExperience, ScoreProjects, ScoreSkills)

            .AddNode(ProposeCommands, async (ctx, ct) =>
            {
                var brief = ctx.Get<string>(BuildBrief);
                var modelRequest = new ModelRequest
                {
                    System = SystemPromptFor(request.Effort),
                    User = brief,
                    SchemaName = "tailor-commands",
                    MaxOutputTokens = MaxOutputTokensFor(request.Effort),
                    Temperature = 0.2,
                    CacheKey = $"tailor:{request.JobId}:{request.BaseResumeId}",
                };

                // Requests TailorCommandParseResultList rather than IReadOnlyList<TailorCommand>
                // directly so one command the model malformed becomes a rejection instead of an
                // exception that discards every other, perfectly good, command it proposed
                // (CONTRACTS.md §6) — see TailorCommandParseResult.
                var response = await languageModel.CompleteAsync<TailorCommandParseResultList>(modelRequest, ct).ConfigureAwait(false);
                ctx.Budget.Record(ProposeCommands, response.Usage);
                return (object?)response.Value;
            })
            .DependsOn(BuildBrief)
            .Critical()

            .AddNode(ValidateCommands, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var parseResults = ctx.Get<TailorCommandParseResultList>(ProposeCommands);
                return Task.FromResult<object?>(commandValidator.Validate(parseResults, doc, options));
            })
            .DependsOn(BuildBase, ProposeCommands)
            .Critical()

            .AddNode(VerifyFabrication, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var validation = ctx.Get<CommandValidationResult>(ValidateCommands);
                return Task.FromResult<object?>(VerifyFabricationOf(doc, validation));
            })
            .DependsOn(ValidateCommands)

            .AddNode(VerifyCoverage, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var analysis = ctx.Get<JobAnalysis>(AnalyzeJd);
                var validation = ctx.Get<CommandValidationResult>(ValidateCommands);
                var executed = commandExecutor.Execute(doc, validation.Accepted);
                return Task.FromResult<object?>(coverageAnalyzer.Analyze(executed.Document, analysis));
            })
            .DependsOn(ValidateCommands)

            .AddNode(ExecuteCommands, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var validation = ctx.Get<CommandValidationResult>(ValidateCommands);
                var executed = commandExecutor.Execute(doc, validation.Accepted);

                // Deterministic headline override (CONTRACTS.md §2 "Tailored headline"),
                // applied here — after execution, before enforce-page-budget and render see
                // the document — using the job posting FetchJd already produced. ValidateCommands
                // transitively depends on FetchJd via BuildBrief/the score-* nodes/AnalyzeJd,
                // so its result is already present without a direct edge, the same way this
                // node already reads BuildBase's document above.
                var posting = ctx.Get<JobPosting>(FetchJd);
                executed = executed with { Document = ApplyTailoredHeadline(executed.Document, posting.Title) };

                // Forced pins/excludes (CONTRACTS.md §6 "Forced inclusion") are applied last,
                // after every model command has run, so they always win regardless of what
                // include/exclude commands the model proposed.
                executed = executed with
                {
                    Document = ApplyForcedInclusion(executed.Document, options.PinnedEntryIds, options.ExcludedEntryIds),
                };

                return Task.FromResult<object?>(executed);
            })
            .DependsOn(ValidateCommands)
            .Critical()

            .AddNode(EnforcePageBudget, async (ctx, ct) =>
            {
                var executed = ctx.Get<CommandExecutionResult>(ExecuteCommands);
                var candidates = new CandidateSet
                {
                    Experience = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreExperience),
                    Projects = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreProjects),
                    Skills = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreSkills),
                };

                var result = await pageBudgetEnforcer
                    .EnforceAsync(executed.Document, executed.Diff, candidates, options, ct)
                    .ConfigureAwait(false);
                return (object?)result;
            })
            .DependsOn(ExecuteCommands, ScoreExperience, ScoreProjects, ScoreSkills)

            .AddNode(Render, async (ctx, ct) =>
            {
                var budgeted = ctx.Get<PageBudgetResult>(EnforcePageBudget);
                var rendered = await resumeRenderer.RenderAsync(budgeted.Document, RenderFormat.Html, ct).ConfigureAwait(false);
                return (object?)rendered;
            })
            .DependsOn(VerifyFabrication, VerifyCoverage, EnforcePageBudget)

            .Build();
    }

    private IReadOnlyList<ScoredCandidate> ScoreSection(GraphContext ctx, Func<CandidateSet, IReadOnlyList<ScoredCandidate>> select)
    {
        // Each score-* node recomputes the full candidate set independently so the three
        // nodes stay genuinely independent of one another in the graph (no edges between
        // them); the cost is negligible since scoring is pure, in-memory C#.
        var doc = ctx.Get<ResumeDocument>(BuildBase);
        var analysis = ctx.Get<JobAnalysis>(AnalyzeJd);
        return select(relevanceScorer.Score(doc, analysis));
    }

    /// <summary>
    /// Builds the system prompt for a given effort: the base instructions plus, at
    /// <see cref="ModelEffort.Thorough"/> and above, permission to propose
    /// <c>injectKeywords</c> (still bound by the fabrication guard and KB-evidence rule
    /// enforced deterministically by <see cref="CommandValidator"/>), and at
    /// <see cref="ModelEffort.Maximum"/>, an instruction to regenerate the summary every
    /// run rather than leaving it as-is (CONTRACTS.md §6's effort table).
    /// </summary>
    /// <summary>
    /// Keeps the output-token cap in step with what each effort licenses. CONTRACTS.md §6's
    /// table gives *typical* output as ~200/~600/~1,200/~2,000 tokens; this caps at roughly
    /// double that, because a cap is a ceiling for the worst case and half of all runs exceed
    /// a typical figure by definition. <see cref="ModelEffort.Maximum"/> alone permits 20
    /// rewrites of up to 300 characters (~1,800 tokens before <c>setSummary</c>'s prose and the
    /// ordering arrays are counted), so a cap set near the typical figure truncates routinely —
    /// which is exactly what a 2,048-token ceiling did here. Floored at the previous fixed
    /// budget so the cheaper tiers are unaffected. Overrun is no longer silent regardless: see
    /// <c>ModelResponseTruncatedException</c>.
    /// </summary>
    private static int MaxOutputTokensFor(ModelEffort effort) => effort switch
    {
        ModelEffort.Maximum => 4096,
        ModelEffort.Thorough => 2048,
        _ => 1024,
    };

    private static string SystemPromptFor(ModelEffort effort)
    {
        if (effort < ModelEffort.Thorough)
        {
            return SystemPrompt;
        }

        var prompt = SystemPrompt +
            " At this effort level you may also propose injectKeywords commands to weave " +
            "job-description keywords into an existing bullet, but only keywords the " +
            "candidate's knowledge base already evidences elsewhere — never a keyword the " +
            "candidate cannot support.";

        return effort == ModelEffort.Maximum
            ? prompt + " Always propose a setSummary command tailored specifically to this job."
            : prompt;
    }

    /// <summary>
    /// Deterministic headline override (CONTRACTS.md §2 "Tailored headline"): when
    /// <paramref name="jobTitle"/> was determined at ingest, <paramref name="document"/>'s
    /// headline becomes that title, trimmed and with internal whitespace runs collapsed to
    /// single spaces — never otherwise rewritten. When <paramref name="jobTitle"/> is null
    /// or blank, <paramref name="document"/> is returned unchanged and the profile
    /// headline <c>build-base</c> copied from the knowledge base stands. Internal so it is
    /// unit-testable without running the whole graph.
    /// </summary>
    internal static ResumeDocument ApplyTailoredHeadline(ResumeDocument document, string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            return document;
        }

        var normalized = string.Join(' ', jobTitle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return document with { Basics = document.Basics with { Headline = normalized } };
    }

    /// <summary>
    /// Deterministic forced-inclusion override (CONTRACTS.md §6 "Forced inclusion"): after
    /// every model command has been applied, an id in <paramref name="pinnedEntryIds"/> has
    /// its entry's <c>Included</c> flag forced to <see langword="true"/>, and an id in
    /// <paramref name="excludedEntryIds"/> has it forced to <see langword="false"/> —
    /// regardless of what the model's include/exclude commands decided. Works generically
    /// across every entry kind that carries an <c>Included</c> flag (<c>exp:</c>, <c>prj:</c>,
    /// <c>edu:</c>, <c>cert:</c>); an id that doesn't match any entry in
    /// <paramref name="document"/> is silently ignored here — the endpoint validates ids
    /// resolve to a real knowledge-base entry before a run ever starts, and the two lists are
    /// mutually exclusive by the same pre-flight check, so this never has to arbitrate a
    /// conflict. A null or empty pair of lists is a no-op that returns <paramref name="document"/>
    /// unchanged. Internal so it is unit-testable without running the whole graph, the same
    /// way <see cref="ApplyTailoredHeadline"/> is.
    /// </summary>
    internal static ResumeDocument ApplyForcedInclusion(
        ResumeDocument document, IReadOnlyList<string>? pinnedEntryIds, IReadOnlyList<string>? excludedEntryIds)
    {
        var pinned = pinnedEntryIds is { Count: > 0 } ? new HashSet<string>(pinnedEntryIds, StringComparer.Ordinal) : null;
        var excluded = excludedEntryIds is { Count: > 0 } ? new HashSet<string>(excludedEntryIds, StringComparer.Ordinal) : null;

        if (pinned is null && excluded is null)
        {
            return document;
        }

        bool? Resolve(string id) =>
            pinned?.Contains(id) == true ? true :
            excluded?.Contains(id) == true ? false :
            null;

        return document with
        {
            Experience = [.. document.Experience.Select(e => Resolve(e.Id) is { } inc ? e with { Included = inc } : e)],
            Projects = [.. document.Projects.Select(p => Resolve(p.Id) is { } inc ? p with { Included = inc } : p)],
            Education = [.. document.Education.Select(e => Resolve(e.Id) is { } inc ? e with { Included = inc } : e)],
            Certifications = [.. document.Certifications.Select(c => Resolve(c.Id) is { } inc ? c with { Included = inc } : c)],
        };
    }

    /// <summary>
    /// Drops candidates belonging to a force-excluded entry (CONTRACTS.md §6 "Forced
    /// inclusion") from a scored candidate list before it reaches <see cref="IBriefBuilder"/>,
    /// so the model never spends a decision on an entry <see cref="ApplyForcedInclusion"/>
    /// will exclude regardless of what it proposes. <paramref name="candidates"/> carries
    /// bullet-level ids (e.g. <c>exp:acme#0</c>); <see cref="Domain.Ids.EntityId.Parent"/>
    /// maps each back to its owning entry id the same way <see cref="PageBudgetEnforcer"/>'s
    /// own entry-scoring already relies on.
    /// </summary>
    private static IReadOnlyList<ScoredCandidate> ExcludeForcedEntries(
        IReadOnlyList<ScoredCandidate> candidates, IReadOnlyList<string> excludedEntryIds)
    {
        if (excludedEntryIds.Count == 0)
        {
            return candidates;
        }

        var excluded = new HashSet<string>(excludedEntryIds, StringComparer.Ordinal);
        return [.. candidates.Where(c => !(EntityId.TryParse(c.EntityId, out var id) && excluded.Contains(id.Parent.ToString())))];
    }

    private FabricationVerification VerifyFabricationOf(ResumeDocument doc, CommandValidationResult validation)
    {
        var violations = new List<string>();

        foreach (var command in validation.Accepted.OfType<RewriteCommand>())
        {
            if (!EntityId.TryParse(command.Target, out var id) || !doc.TryFindBullet(id, out var bullet))
            {
                continue;
            }

            if (!fabricationGuard.IsSafe(bullet.Text, command.Text, out var reason))
            {
                violations.Add($"{command.Target}: {reason}");
            }
        }

        return new FabricationVerification { Passed = violations.Count == 0, Violations = violations };
    }
}
