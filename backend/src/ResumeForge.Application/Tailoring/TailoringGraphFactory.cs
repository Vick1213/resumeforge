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

        var options = request.MaxRewrites is { } maxRewrites
            ? tailorOptions with { MaxRewrites = maxRewrites }
            : tailorOptions;

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
                var candidates = new CandidateSet
                {
                    Experience = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreExperience),
                    Projects = ctx.Get<IReadOnlyList<ScoredCandidate>>(ScoreProjects),
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
                    System = SystemPrompt,
                    User = brief,
                    SchemaName = "tailor-commands",
                    MaxOutputTokens = 1024,
                    Temperature = 0.2,
                    CacheKey = $"tailor:{request.JobId}:{request.BaseResumeId}",
                };

                var response = await languageModel.CompleteAsync<IReadOnlyList<TailorCommand>>(modelRequest, ct).ConfigureAwait(false);
                ctx.Budget.Record(ProposeCommands, response.Usage);
                return (object?)response.Value;
            })
            .DependsOn(BuildBrief)
            .Critical()

            .AddNode(ValidateCommands, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var commands = ctx.Get<IReadOnlyList<TailorCommand>>(ProposeCommands);
                return Task.FromResult<object?>(commandValidator.Validate(commands, doc, options));
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
                return Task.FromResult<object?>(commandExecutor.Execute(doc, validation.Accepted));
            })
            .DependsOn(ValidateCommands)
            .Critical()

            .AddNode(Render, async (ctx, ct) =>
            {
                var executed = ctx.Get<CommandExecutionResult>(ExecuteCommands);
                var rendered = await resumeRenderer.RenderAsync(executed.Document, RenderFormat.Html, ct).ConfigureAwait(false);
                return (object?)rendered;
            })
            .DependsOn(VerifyFabrication, VerifyCoverage, ExecuteCommands)

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
