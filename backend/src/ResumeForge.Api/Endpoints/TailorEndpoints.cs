using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/tailor</c> routes (CONTRACTS.md §9).</summary>
public static class TailorEndpoints
{
    /// <summary>Registers the tailoring routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapTailorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tailor").WithTags("Tailor");

        group.MapPost("/", RunAsync)
            .WithName("RunTailoring")
            .Produces<TailoringResult>();

        group.MapGet("/{runId}/trace", GetTraceAsync)
            .WithName("GetTailoringTrace")
            .Produces<IReadOnlyList<GraphNodeTrace>>();

        return app;
    }

    private static async Task<IResult> RunAsync(
        TailorRequest request,
        IJobRepository jobRepository,
        IResumeRepository resumeRepository,
        ITailoringRunRepository runRepository,
        ITailoringService tailoringService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var job = await jobRepository.GetAsync(request.JobId, ct).ConfigureAwait(false);
        if (job is null)
        {
            return ProblemResults.NotFound($"No job posting with id '{request.JobId}' was found.");
        }

        if (request.BaseResumeId is { } baseResumeId)
        {
            var baseResume = await resumeRepository.GetAsync(baseResumeId, ct).ConfigureAwait(false);
            if (baseResume is null)
            {
                return ProblemResults.NotFound($"No resume with id '{baseResumeId}' was found.");
            }
        }

        var serviceRequest = new TailoringRequest
        {
            JobId = request.JobId,
            BaseResumeId = request.BaseResumeId,
            MaxRewrites = request.MaxRewrites,
            Effort = request.Effort,
        };

        var result = await tailoringService.TailorAsync(serviceRequest, ct).ConfigureAwait(false);

        if (request.DryRun)
        {
            return TypedResults.Ok(result);
        }

        // CommandExecutor.Build (Application layer) preserves the source document's Id and
        // Name unchanged, so result.Document carries the exact Id/Name of the base resume
        // it started from. Persisting it as-is would silently overwrite that base resume
        // instead of creating a new tailored variant (ResumeForgeDbContext's own doc
        // comments describe "the base resume and every tailored variant" as distinct rows).
        // This endpoint therefore assigns the persisted variant a new id and a name derived
        // from the job posting; see the implementation report for this ruling.
        //
        // A raw-text job posting (as opposed to one fetched from a URL) never has Title or
        // Company populated, so falling back to the job's own id would put a bare GUID in
        // a user-visible name — DeriveNameFromRawText instead reads the posting's own text
        // for something a human would recognize.
        var tailoredName = job.Company is { Length: > 0 } company && job.Title is { Length: > 0 } title
            ? $"{company} - {title}"
            : job.Title ?? job.Company ?? DeriveNameFromRawText(job.RawText);

        var toPersist = result.Document with
        {
            Id = Guid.NewGuid().ToString(),
            Name = tailoredName,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        // A tailoring run's output is a new variant, never the base resume, regardless of
        // whether the source document it started from happened to be the base.
        await resumeRepository.SaveAsync(toPersist, isBase: false, ct).ConfigureAwait(false);

        var runId = await runRepository.SaveAsync(
            new TailoringRunRecord
            {
                Id = string.Empty,
                JobId = request.JobId,
                BaseResumeId = request.BaseResumeId,
                Result = result,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            ct).ConfigureAwait(false);

        // TailoringResult (CONTRACTS.md §6) carries no run id, so there is no field to put
        // it in on the response body itself; it is surfaced via the Location header instead
        // so a client that wants GET /api/tailor/{runId}/trace later can still find it.
        httpContext.Response.Headers.Location = $"/api/tailor/{runId}/trace";

        var responseResult = result with { Document = toPersist };
        return TypedResults.Ok(responseResult);
    }

    private static async Task<IResult> GetTraceAsync(string runId, ITailoringRunRepository repository, CancellationToken ct)
    {
        var trace = await repository.GetTraceAsync(runId, ct).ConfigureAwait(false);

        return trace is null
            ? ProblemResults.NotFound($"No tailoring run with id '{runId}' was found.")
            : TypedResults.Ok(trace);
    }

    private const int MaxDerivedNameLength = 80;

    /// <summary>
    /// Phrases that typically introduce a role title in JD boilerplate ("We are hiring a
    /// Senior Backend Engineer..."). Checked in order; the first match in the line wins.
    /// </summary>
    private static readonly string[] TitleIntroPhrases =
    [
        "hiring a ", "hiring an ", "seeking a ", "seeking an ",
        "looking for a ", "looking for an ",
        "join us as a ", "join us as an ", "join as a ", "join as an ",
        "the role of ", "position of ", "role of ", "title of ",
    ];

    /// <summary>
    /// Common job-title nouns. A run of Title Case words ending in one of these is treated
    /// as a confident title match even without <see cref="SeniorityClassifier"/> agreeing
    /// (an entry-level title carries no seniority cue at all).
    /// </summary>
    private static readonly HashSet<string> TitleNouns = new(StringComparer.Ordinal)
    {
        "Engineer", "Developer", "Manager", "Analyst", "Designer", "Scientist", "Architect",
        "Specialist", "Consultant", "Administrator", "Coordinator", "Director", "Lead",
        "Intern", "Associate", "Representative", "Executive", "Producer", "Recruiter",
        "Accountant", "Technician",
    };

    /// <summary>Lowercase connector words allowed mid-title ("Head of Engineering").</summary>
    private static readonly HashSet<string> TitleConnectorWords =
        new(StringComparer.OrdinalIgnoreCase) { "of", "and", "for", "&" };

    /// <summary>
    /// Names a tailored resume from a job posting whose <c>Title</c>/<c>Company</c> could
    /// not be determined (always the case for a raw-text posting; occasionally the case
    /// for a URL fetch that didn't yield structured fields). The posting's first non-blank
    /// line is usually the role title or an opening sentence naming the role. A bare GUID
    /// must never appear in a user-visible name; beyond that, a full prose sentence
    /// ("We are hiring a Senior Backend Engineer to join our platform team.") is a poor
    /// resume name, so a detected role title is preferred where one is available, and
    /// otherwise the line is trimmed rather than kept whole with trailing punctuation.
    /// </summary>
    private static string DeriveNameFromRawText(string rawText)
    {
        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryExtractTitlePhrase(line, out var title))
            {
                return Truncate(title);
            }

            // No detectable title: keep the line as-is when it already reads like one
            // (short, no terminal sentence punctuation) — this is the common case for a
            // well-formed posting whose first line already is the role title. Otherwise
            // trim the trailing sentence punctuation rather than keep a whole sentence
            // with a period on the end.
            var trimmed = line.Length > 0 && line[^1] is '.' or '!' or '?'
                ? line[..^1].TrimEnd()
                : line;

            return Truncate(trimmed);
        }

        // RawText is required and CreateJob rejects a whitespace-only value, so this is
        // reachable only for a degenerate posting; still, never fall back to the job id.
        return "Tailored Resume";
    }

    /// <summary>
    /// Looks for one of <see cref="TitleIntroPhrases"/> in <paramref name="line"/> and, if
    /// found, greedily consumes the following run of Title Case words (allowing a small set
    /// of lowercase connectors) as a candidate role title. The candidate is accepted only
    /// when it ends in a recognized <see cref="TitleNouns"/> entry or
    /// <see cref="SeniorityClassifier"/> recognizes it as a titled role, so an accidental
    /// phrase match never produces a nonsense name.
    /// </summary>
    private static bool TryExtractTitlePhrase(string line, out string title)
    {
        var lower = line.ToLowerInvariant();

        foreach (var phrase in TitleIntroPhrases)
        {
            var index = lower.IndexOf(phrase, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var words = new List<string>();
            var cursor = index + phrase.Length;

            while (cursor < line.Length && words.Count < 6)
            {
                while (cursor < line.Length && line[cursor] == ' ')
                {
                    cursor++;
                }

                var wordStart = cursor;
                while (cursor < line.Length && line[cursor] != ' ')
                {
                    cursor++;
                }

                if (cursor == wordStart)
                {
                    break;
                }

                var word = line[wordStart..cursor].TrimEnd('.', ',', ';', ':', '!', '?');
                if (word.Length == 0 || !(char.IsUpper(word[0]) || TitleConnectorWords.Contains(word)))
                {
                    break;
                }

                words.Add(word);

                if (TitleNouns.Contains(word))
                {
                    break;
                }
            }

            if (words.Count == 0)
            {
                continue;
            }

            var candidate = string.Join(' ', words);
            if (TitleNouns.Contains(words[^1]) || SeniorityClassifier.FromTitle(candidate) != SeniorityLevel.Unknown)
            {
                title = candidate;
                return true;
            }
        }

        title = string.Empty;
        return false;
    }

    private static string Truncate(string name) =>
        name.Length <= MaxDerivedNameLength
            ? name
            : string.Concat(name.AsSpan(0, MaxDerivedNameLength - 1), "…");
}
