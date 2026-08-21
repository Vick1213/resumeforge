using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Formatting;
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

    /// <summary>
    /// Node name: the ATS/recruiter gap scan — the first of the run's two model calls
    /// (CONTRACTS.md §6 "ATS review pass").
    /// </summary>
    public const string AtsReview = "ats-review";

    /// <summary>Node name: builds the compact model brief.</summary>
    public const string BuildBrief = "build-brief";

    /// <summary>Node name: the only node that calls <see cref="ILanguageModel"/>.</summary>
    public const string ProposeCommands = "propose-commands";

    /// <summary>Node name: validates the proposed commands.</summary>
    public const string ValidateCommands = "validate-commands";

    /// <summary>
    /// Node name: one bounded second model call that re-proposes the commands
    /// <c>validate-commands</c> rejected for fixable reasons — a ragged line, an over-long
    /// rewrite, a fabricated figure. Without it, every improvement the model proposed and
    /// the validator refused silently vanished: the user watched the model describe a
    /// better bullet and then received a resume without it.
    /// </summary>
    public const string RepairCommands = "repair-commands";

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

    /// <summary>
    /// The character bands a bullet must land in to fill whole lines, stated in the prompt
    /// because the model cannot see the rendered page and a stub last line is invisible to it
    /// otherwise. Derived from <see cref="BulletLineFit"/> rather than written out, so the
    /// instruction and the <c>ragged-line-fill</c> rejection that enforces it can never drift
    /// apart. Only the one- and two-line bands are offered: a three-line bullet would need
    /// more than the 300 characters a rewrite is allowed.
    /// </summary>
    private static readonly string LineFillRule = BuildLineFillRule();

    private const string SystemPrompt =
        "You tailor a resume to a job by emitting a JSON array of commands only. You never " +
        "write resume prose except through the rewrite command, and only when no existing " +
        "variant fits. Prefer selectVariant over rewrite. Reference only the requirement and " +
        "candidate ids given to you. Do not invent metrics or facts. " +
        "Never exclude an experience entry: employment history is the substance of the " +
        "resume, a dropped job reads as a gap, and the validator rejects the attempt anyway. " +
        "Tailoring means re-emphasizing, not thinning — a dense page that evidences the " +
        "posting beats a sparse one, so keep bullets and entries unless one is genuinely " +
        "irrelevant to this job, and prefer sharpening a weak bullet over deleting it. " +
        "Never drop a number: a bullet's figures — latency, throughput, error rate, users, " +
        "volume, money, time — are the evidence, and every variant you pick or line you write " +
        "must carry the ones the original had. Where a candidate bullet already quantifies a " +
        "result, prefer it over one that only describes the work. " +
        "The brief gives you the JOB header, the REQUIREMENTS a deterministic extractor found, " +
        "and POSTING, the employer's own text. The extractor is a heuristic: it misses " +
        "requirements and sometimes splits one badly, so where the two disagree the POSTING " +
        "wins. Judge every decision — what to include, what order, which variant, what to " +
        "rewrite — by how well it evidences what this specific posting asks for, favouring " +
        "its stated must-haves and its own vocabulary over generic strength. " +
        // The findings of the ats-review pass, and the standing instruction for what to do
        // with them. Stated unconditionally rather than only when the section is present:
        // the rule it describes — a keyword belongs in a bullet, in context, with a number —
        // is how a resume should read whether or not a review pass ran to point at specific
        // terms, and a prompt that changes shape between runs is harder to reason about than
        // one that does not.
        "A resume has to clear two gates and they fail it for different reasons. An applicant " +
        "tracking system matches the posting's vocabulary against the document's, so a term the " +
        "posting leans on has to appear somewhere. A recruiter reads for evidence, so the same " +
        "term has to appear inside a bullet, attached to what was built and what it moved — a " +
        "keyword parked in the skills list clears the parser and tells a human nothing. When the " +
        "brief carries an ATS-GAPS section it lists exactly what a screener found missing: one " +
        "line per gap, as keyword|importance|skills-only|placement|angle. Work through it from " +
        "critical down, and close each gap where the angle says it belongs — by rewriting or " +
        "reselecting the bullet named in placement so the keyword lands in a sentence that keeps " +
        "the original's numbers. A gap flagged S is already in the skills list and already passes " +
        "the parser; adding it there again accomplishes nothing, and the only thing that closes it " +
        "is a bullet. Every claim must still be supported by the candidate's existing text: if the " +
        "angle asks for something the candidate's material does not support, leave that gap open.";

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

            .AddNode(AtsReview, async (ctx, ct) =>
            {
                if (!options.Effort.RunsAtsReview())
                {
                    return null;
                }

                var posting = ctx.Get<JobPosting>(FetchJd);
                var baseDoc = ctx.Get<ResumeDocument>(BuildBase);

                // The review must see the resume the user is actually sending: an entry the
                // user force-excluded (their per-entry Never/unselect choice) is not on the
                // page, and a review that keeps flagging it reads as the tool ignoring the
                // user. Forced pins/excludes are applied to the review's copy up front, the
                // same override ExecuteCommands applies to the real document later.
                var reviewDoc = ApplyForcedInclusion(baseDoc, options.PinnedEntryIds, options.ExcludedEntryIds);

                return (object?)await RunAtsReviewAsync(ctx, posting, reviewDoc, options, ct).ConfigureAwait(false);
            })
            .DependsOn(FetchJd, BuildBase)

            .AddNode(BuildBrief, (ctx, _) =>
            {
                var posting = ctx.Get<JobPosting>(FetchJd);
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

                // Null when the review did not run or could not complete: the brief simply
                // omits its section and the command pass works from the posting alone, which
                // is exactly the behaviour that existed before the review pass was added.
                ctx.TryGet<AtsReview?>(AtsReview, out var review);

                var brief = briefBuilder.Build(posting, analysis, candidates, baseDoc, options, review);
                return Task.FromResult<object?>(brief);
            })
            .DependsOn(FetchJd, ScoreExperience, ScoreProjects, ScoreSkills, AtsReview)

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

            .AddNode(RepairCommands, async (ctx, ct) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var validation = ctx.Get<CommandValidationResult>(ValidateCommands);
                var repaired = await RepairRejectedCommandsAsync(ctx, doc, validation, options, request, ct).ConfigureAwait(false);
                return (object?)repaired;
            })
            .DependsOn(BuildBase, ValidateCommands)
            .Critical()

            .AddNode(VerifyFabrication, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var validation = ctx.Get<CommandValidationResult>(RepairCommands);
                return Task.FromResult<object?>(VerifyFabricationOf(doc, validation));
            })
            .DependsOn(RepairCommands)

            .AddNode(VerifyCoverage, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var analysis = ctx.Get<JobAnalysis>(AnalyzeJd);
                var validation = ctx.Get<CommandValidationResult>(RepairCommands);
                var executed = commandExecutor.Execute(doc, validation.Accepted);
                return Task.FromResult<object?>(coverageAnalyzer.Analyze(executed.Document, analysis));
            })
            .DependsOn(RepairCommands)

            .AddNode(ExecuteCommands, (ctx, _) =>
            {
                var doc = ctx.Get<ResumeDocument>(BuildBase);
                var validation = ctx.Get<CommandValidationResult>(RepairCommands);
                var executed = commandExecutor.Execute(doc, validation.Accepted);

                // Deterministic headline override (CONTRACTS.md §2 "Tailored headline"),
                // applied here — after execution, before enforce-page-budget and render see
                // the document — using the job posting FetchJd already produced. ValidateCommands
                // transitively depends on FetchJd via BuildBrief/the score-* nodes/AnalyzeJd,
                // so its result is already present without a direct edge, the same way this
                // node already reads BuildBase's document above. A user-supplied headline on
                // the request wins over the posting's title: the title a posting was scraped
                // with is a default, not a decision, and the user asked for a way to make one.
                var posting = ctx.Get<JobPosting>(FetchJd);
                var headline = string.IsNullOrWhiteSpace(request.Headline) ? posting.Title : request.Headline;
                executed = executed with { Document = ApplyTailoredHeadline(executed.Document, headline) };

                // Forced pins/excludes (CONTRACTS.md §6 "Forced inclusion") are applied last,
                // after every model command has run, so they always win regardless of what
                // include/exclude commands the model proposed.
                executed = executed with
                {
                    Document = ApplyForcedInclusion(executed.Document, options.PinnedEntryIds, options.ExcludedEntryIds),
                };

                return Task.FromResult<object?>(executed);
            })
            .DependsOn(RepairCommands)
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

    /// <summary>
    /// The <c>ats-review</c> pass's system prompt: read one resume against one posting the
    /// way the two gates that actually reject people read it, and report what is missing.
    /// </summary>
    /// <remarks>
    /// The prompt spends most of its length on the distinction between those two gates,
    /// because collapsing them is the specific failure this pass exists to prevent. A model
    /// asked only "what keywords are missing?" answers with a list that can be satisfied by
    /// appending terms to the skills section — which clears the parser and leaves a human
    /// looking at a resume that claims a technology and shows no evidence of ever using it.
    /// Naming the human gate explicitly, and requiring an <c>angle</c> drawn from the
    /// candidate's own material for every gap, is what makes the output actionable as bullet
    /// text rather than as list padding.
    ///
    /// It is also told, twice, that it may not invent anything: the review is read by a
    /// second call that has the authority to rewrite bullets, so a fabricated angle here
    /// becomes a fabricated claim there. The deterministic fabrication guard would catch a
    /// rewrite that adds a number, but not one that adds a technology the candidate has
    /// never touched — that bar has to be held here.
    /// </remarks>
    private const string AtsReviewSystemPrompt =
        "You are screening one resume against one job posting, and you report findings only — " +
        "you never rewrite the resume. Two different gates reject candidates, and you assess both.\n\n" +
        "GATE 1, the applicant tracking system: it matches the posting's vocabulary against the " +
        "resume's. A term the posting leans on that the resume never says costs the candidate the " +
        "screen regardless of whether they can do the work. Judge this on the posting's own words, " +
        "including the spelling it uses.\n\n" +
        "GATE 2, the recruiter or hiring manager: they read for evidence, not for terms. A keyword " +
        "sitting in a skills list satisfies gate 1 and tells a human nothing — no scale, no outcome, " +
        "no account of where it was actually used. A term clears gate 2 only when it appears inside a " +
        "bullet attached to what was built and what it moved. When the resume lists a term among its " +
        "skills but no bullet anywhere puts it in context, that is still a gap: report it with " +
        "skillsOnly true. It is the most valuable kind of gap you can find, because the material to " +
        "fix it usually already exists.\n\n" +
        "For every gap, give an angle: the specific claim that keyword should be evidencing, built " +
        "ONLY from work the resume already describes — the project or job it belongs to, what the " +
        "candidate did there, and the metric already on the page that shows it mattered. Never " +
        "invent an employer, a technology, a date, or a number. If the resume gives you nothing to " +
        "support a term the posting wants, say so in the angle rather than inventing support for it; " +
        "an honest \"no evidence anywhere in this resume\" is a useful finding and a fabricated one " +
        "is worse than silence.\n\n" +
        "Score both states out of 100: scoreBefore is the resume as it stands, scoreAfter is the same " +
        "resume with every gap you listed closed. Score honestly — scoreAfter is a ceiling for this " +
        "candidate's real material, not a round number, and it should stay well under 100 when the " +
        "posting asks for experience this person genuinely does not have.";

    /// <summary>
    /// Runs the <c>ats-review</c> pass, returning null when it cannot complete.
    /// </summary>
    /// <remarks>
    /// Deliberately swallowing: this pass improves the brief and is not required to produce
    /// one. Letting it throw would mark the node failed, and the executor skips a failed
    /// node's transitive dependents — which here is <c>build-brief</c> and therefore the
    /// entire rest of the pipeline. A provider hiccup on an advisory call must not cost the
    /// user their tailored resume, so the failure is confined to this method and the run
    /// continues exactly as it did before this pass existed.
    ///
    /// Unlike every other model call in the product, this one is given the resume in full.
    /// The token asymmetry <see cref="IBriefBuilder"/> exists to maintain does not apply:
    /// that brief withholds the resume because the command pass decides *about* nodes it can
    /// address by id, whereas this pass's entire question is what the document as a whole
    /// fails to say — which cannot be answered from a list of ids and truncated previews.
    /// </remarks>
    private async Task<AtsReview?> RunAtsReviewAsync(
        GraphContext ctx, JobPosting posting, ResumeDocument baseDoc, TailorOptions options, CancellationToken ct)
    {
        try
        {
            // Markdown rather than PDF or HTML: it is the plain, structurally-labelled form
            // of the same document — headings, entries, and bullets with no layout noise —
            // which is as close as this codebase gets to what a parser extracts.
            var rendered = await resumeRenderer.RenderAsync(baseDoc, RenderFormat.Markdown, ct).ConfigureAwait(false);
            var resumeText = System.Text.Encoding.UTF8.GetString(rendered.Content);

            var user =
                $"JOB TITLE: {posting.Title}\nCOMPANY: {posting.Company}\n\n" +
                $"JOB POSTING\n{Truncate(posting.RawText, options.PostingExcerptChars)}\n\n" +
                $"RESUME (ids in parentheses are addressable; use them for placement)\n{resumeText}";

            var response = await languageModel.CompleteAsync<AtsReview>(
                new ModelRequest
                {
                    System = AtsReviewSystemPrompt,
                    User = user,
                    SchemaName = "ats-review",
                    MaxOutputTokens = AtsReviewMaxOutputTokens,

                    // Higher than the command pass's 0.2. This call is looking for what is
                    // absent, and absence is exactly what a near-greedy decode is worst at
                    // surfacing — it re-reports the obvious keyword matches run after run.
                    Temperature = 0.4,
                    CacheKey = $"ats-review:{posting.Id}:{baseDoc.Id}",
                },
                ct).ConfigureAwait(false);

            ctx.Budget.Record(AtsReview, response.Usage);
            return response.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The output ceiling for the review. Sized for the worst realistic case — roughly 20
    /// gaps, each carrying a keyword, a placement id, and a sentence-long angle, plus the
    /// verdict and recruiter notes — rather than for a typical one, on the same reasoning as
    /// <see cref="MaxOutputTokensFor"/>.
    /// </summary>
    private const int AtsReviewMaxOutputTokens = 4096;

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "…(posting truncated)");
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
        // Full rewrites every bullet it touches and may retagline every project, so its
        // ceiling is sized from the work rather than from a typical figure: ~40 bullets at
        // ~90 tokens per rewrite command, plus taglines and a summary, lands near 5k. 10240
        // leaves the headroom that made every tier below it truncate when it was missing.
        ModelEffort.Full => 10240,
        ModelEffort.Maximum => 4096,
        ModelEffort.Thorough => 2048,
        _ => 1024,
    };

    private static string BuildLineFillRule()
    {
        var (oneMin, oneMax) = BulletLineFit.TargetLength(1);
        var (twoMin, twoMax) = BulletLineFit.TargetLength(2);

        return
            $" Any text you write for a bullet is set in a fixed-width column about {BulletLineFit.CharsPerLine} " +
            "characters wide, so its length decides where it wraps. Write it to fill whole lines: " +
            $"{oneMin}-{oneMax} characters, or {twoMin}-{twoMax} characters. Never land between " +
            $"{oneMax + 1} and {twoMin - 1} — that spills two or three words onto a line of their own and " +
            "leaves the rest of it blank. Reaching a band by padding is not the point: tighten or expand " +
            "the claim itself, and prefer the shorter band when the longer one would need filler.";
    }

    private static string SystemPromptFor(ModelEffort effort)
    {
        if (effort < ModelEffort.Thorough)
        {
            return SystemPrompt + LineFillRule;
        }

        var prompt = SystemPrompt + LineFillRule +
            " At this effort level you may also propose injectKeywords commands to weave " +
            "job-description keywords into an existing bullet, but only keywords the " +
            "candidate's knowledge base already evidences elsewhere — never a keyword the " +
            "candidate cannot support.";

        if (effort < ModelEffort.Maximum)
        {
            return prompt;
        }

        prompt += " Always propose a setSummary command tailored specifically to this job.";

        return effort == ModelEffort.Full
            ? prompt +
              " This run is Full effort: treat every line as yours to improve. Rewrite any " +
              "bullet whose phrasing does not serve this posting, on every included entry — " +
              "you are not rationing rewrites here. The TAGLINES section lists each project's " +
              "id and its current one-line description; propose a setTagline command, keyed " +
              "by that project id, for any whose description could speak more directly to " +
              "this job. The single " +
              "rule that does not relax: every claim must already be supported by the " +
              "candidate's existing text. Rephrase, reframe, and re-emphasize freely; never " +
              "add a metric, technology, employer, or date that is not already there."
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

    /// <summary>
    /// Rejection codes worth a second model call: each marks a command whose <em>text</em>
    /// failed a deterministic check the model can satisfy on another attempt. Codes like
    /// <c>unknown-target</c>, <c>op-unavailable-at-effort</c>, or <c>experience-protected</c>
    /// are structural — retrying them re-litigates a rule that will never relax — so they
    /// stay rejected without another call.
    /// </summary>
    private static readonly HashSet<string> RepairableCodes = new(StringComparer.Ordinal)
    {
        "ragged-line-fill",
        "rewrite-too-long",
        "rewrite-multiline",
        "fabricated-metric",
    };

    private const string RepairSystemPrompt =
        "A previous pass proposed resume edits, and a deterministic validator rejected the ones " +
        "listed in this brief — each with the rule it broke. Re-propose a corrected command for " +
        "each rejection you can honestly fix: keep the same op and target id, keep every figure " +
        "the CURRENT text carries, never add a metric, technology, employer, or date that the " +
        "CURRENT text does not already state, and satisfy the REASON given. If a rejection " +
        "cannot be fixed without inventing something, omit it. Reply with a JSON array of " +
        "commands only.";

    /// <summary>
    /// The bounded repair round behind the <see cref="RepairCommands"/> node. One extra model
    /// call, only when the first round left fixable rejections, and advisory in the same way
    /// <see cref="RunAtsReviewAsync"/> is: any failure — provider, parse, truncation — returns
    /// the original validation unchanged, so the worst case is exactly the behaviour that
    /// existed before this pass did.
    /// </summary>
    private async Task<CommandValidationResult> RepairRejectedCommandsAsync(
        GraphContext ctx,
        ResumeDocument doc,
        CommandValidationResult validation,
        TailorOptions options,
        TailoringRequest request,
        CancellationToken ct)
    {
        var repairable = validation.Rejected
            .Where(r => r.Command is not null && RepairableCodes.Contains(r.Code))
            .ToList();

        // Minimal effort cannot rewrite at all, so there is nothing a repair round could
        // legally re-propose.
        if (repairable.Count == 0 || options.Effort == ModelEffort.Minimal)
        {
            return validation;
        }

        try
        {
            var response = await languageModel.CompleteAsync<TailorCommandParseResultList>(
                new ModelRequest
                {
                    System = RepairSystemPrompt + LineFillRule,
                    User = BuildRepairBrief(doc, repairable),
                    SchemaName = "tailor-commands",
                    MaxOutputTokens = MaxOutputTokensFor(options.Effort),
                    Temperature = 0.2,
                    CacheKey = $"repair:{request.JobId}:{request.BaseResumeId}",
                },
                ct).ConfigureAwait(false);

            ctx.Budget.Record(RepairCommands, response.Usage);

            // The repair round spends whatever rewrite budget the first round left, so the
            // two rounds together can never exceed the run's MaxRewrites.
            var remainingRewrites = Math.Max(0, options.MaxRewrites - validation.Accepted.OfType<RewriteCommand>().Count());
            var repairValidation = commandValidator.Validate(
                response.Value, doc, options with { MaxRewrites = remainingRewrites });

            if (repairValidation.Accepted.Count == 0)
            {
                return validation;
            }

            // A rejection whose target the repair round successfully re-proposed is resolved
            // and drops out of the rejected list; everything else — structural rejections,
            // and fixables the second round also failed — stays, so the UI never claims a
            // rejection was fixed when it was not.
            var fixedTargets = new HashSet<string>(
                repairValidation.Accepted.Select(RepairTargetOf).Where(t => t is not null)!,
                StringComparer.Ordinal);

            var remainingRejected = validation.Rejected
                .Where(r => !(r.Command is not null &&
                              RepairableCodes.Contains(r.Code) &&
                              RepairTargetOf(r.Command) is { } target &&
                              fixedTargets.Contains(target)))
                .ToList();

            return new CommandValidationResult
            {
                Accepted = [.. validation.Accepted, .. repairValidation.Accepted],
                Rejected = remainingRejected,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return validation;
        }
    }

    /// <summary>
    /// The node a prose-bearing command writes to, used to match a repair-round acceptance
    /// back to the first-round rejection it resolves. <c>"sum"</c> stands in for the summary,
    /// which has no target id of its own. Type-agnostic on purpose: a rejected rewrite fixed
    /// by a selectVariant on the same bullet is still fixed.
    /// </summary>
    private static string? RepairTargetOf(TailorCommand command) => command switch
    {
        RewriteCommand rw => rw.Target,
        InjectKeywordsCommand inj => inj.Target,
        SelectVariantCommand sv => sv.Target,
        SetTaglineCommand tag => tag.Target,
        SetSummaryCommand => "sum",
        _ => null,
    };

    /// <summary>
    /// One block per rejection: the op and target, the rule it broke, the text currently on
    /// the page (the fabrication baseline the fix must not exceed), and the text that was
    /// rejected. Same delimiter-separated plain-text shape as <see cref="IBriefBuilder"/>'s
    /// brief, for the same token-economy reason.
    /// </summary>
    private static string BuildRepairBrief(ResumeDocument doc, IReadOnlyList<RejectedCommand> repairable)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var rejection in repairable)
        {
            var (op, target, proposed, keywords) = rejection.Command switch
            {
                RewriteCommand rw => ("rewrite", rw.Target, rw.Text, (IReadOnlyList<string>?)null),
                InjectKeywordsCommand inj => ("injectKeywords", inj.Target, inj.Text, inj.Keywords),
                SetTaglineCommand tag => ("setTagline", tag.Target, tag.Text, null),
                SetSummaryCommand sum => ("setSummary", "sum", sum.Text, null),
                _ => (null, null, null, null),
            };

            if (op is null)
            {
                continue;
            }

            sb.Append("REJECTED|").Append(op).Append('|').Append(target).Append('|').Append(rejection.Code).Append('\n');
            sb.Append("REASON|").Append(rejection.Reason).Append('\n');
            sb.Append("CURRENT|").Append(CurrentTextOf(doc, rejection.Command!)).Append('\n');
            sb.Append("PROPOSED|").Append(proposed).Append('\n');

            if (keywords is { Count: > 0 })
            {
                sb.Append("KEYWORDS|").Append(string.Join(",", keywords)).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The text the rejected command would have replaced, as it stands in the document.</summary>
    private static string CurrentTextOf(ResumeDocument doc, TailorCommand command) => command switch
    {
        RewriteCommand rw when EntityId.TryParse(rw.Target, out var id) && doc.TryFindBullet(id, out var b) => b.Text,
        InjectKeywordsCommand inj when EntityId.TryParse(inj.Target, out var id) && doc.TryFindBullet(id, out var b) => b.Text,
        SetTaglineCommand tag => doc.Projects.FirstOrDefault(p => p.Id == tag.Target)?.Tagline ?? string.Empty,
        SetSummaryCommand => doc.Summary ?? string.Empty,
        _ => string.Empty,
    };

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
