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
        IKnowledgeBaseReader knowledgeBaseReader,
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

        var forcedInclusionError = await ValidateForcedInclusionIdsAsync(request, knowledgeBaseReader, ct).ConfigureAwait(false);
        if (forcedInclusionError is not null)
        {
            return forcedInclusionError;
        }

        var serviceRequest = new TailoringRequest
        {
            JobId = request.JobId,
            BaseResumeId = request.BaseResumeId,
            MaxRewrites = request.MaxRewrites,
            Effort = request.Effort,
            MaxPages = request.MaxPages,
            Headline = request.Headline,
            PinnedEntryIds = request.PinnedEntryIds,
            ExcludedEntryIds = request.ExcludedEntryIds,
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

    /// <summary>
    /// Pre-flight validation for <see cref="TailorRequest.PinnedEntryIds"/> and
    /// <see cref="TailorRequest.ExcludedEntryIds"/> (CONTRACTS.md §6 "Forced inclusion"),
    /// following the same pattern <see cref="RunAsync"/> already uses to 404 an unknown
    /// <c>JobId</c>/<c>BaseResumeId</c> before ever building the tailoring graph: look the
    /// ids up against a repository the endpoint already has access to (here,
    /// <see cref="IKnowledgeBaseReader"/>, the same source <c>GET /api/knowledge</c> reads)
    /// and fail fast with a 400 naming exactly what was wrong, rather than letting a bad id
    /// silently no-op deep inside the tailoring graph's forced-inclusion override.
    /// Returns null when both lists are valid (or absent).
    /// </summary>
    private static async Task<IResult?> ValidateForcedInclusionIdsAsync(
        TailorRequest request, IKnowledgeBaseReader knowledgeBaseReader, CancellationToken ct)
    {
        var pinned = request.PinnedEntryIds ?? [];
        var excluded = request.ExcludedEntryIds ?? [];

        if (pinned.Count == 0 && excluded.Count == 0)
        {
            return null;
        }

        var both = pinned.Intersect(excluded, StringComparer.Ordinal).ToList();
        if (both.Count > 0)
        {
            return ProblemResults.BadRequest(
                $"The following id(s) appear in both pinnedEntryIds and excludedEntryIds: {string.Join(", ", both)}.");
        }

        var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
        var knownIds = new HashSet<string>(snapshot.Items.Select(i => i.Id.ToString()), StringComparer.Ordinal);

        var unknown = pinned.Concat(excluded)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !knownIds.Contains(id))
            .ToList();

        return unknown.Count > 0
            ? ProblemResults.BadRequest(
                $"The following id(s) do not resolve to a knowledge base entry: {string.Join(", ", unknown)}.")
            : null;
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
    /// Generic fallback used whenever no role title or company name can be extracted from
    /// the posting's text at all (CONTRACTS.md has no naming convention to defer to here).
    /// </summary>
    private const string GenericFallbackName = "Tailored Resume";

    /// <summary>A candidate fallback line is rejected as "prose" past this many words.</summary>
    private const int MaxFallbackLineWords = 12;

    /// <summary>Labels that introduce a role title on their own line in a well-formed posting.</summary>
    private static readonly string[] TitleLabels = ["job title", "title", "position", "role"];

    /// <summary>Legal-entity suffixes recognized when extracting a company name as a last resort.</summary>
    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LLC", "L.L.C.", "Inc", "Inc.", "Incorporated", "Corp", "Corp.", "Corporation",
        "Ltd", "Ltd.", "Co", "Co.", "LLP", "PLC",
    };

    /// <summary>
    /// Names a tailored resume from a job posting whose <c>Title</c>/<c>Company</c> could
    /// not be determined (always the case for a raw-text posting; occasionally the case for
    /// a URL fetch that didn't yield structured fields). A bare GUID must never appear in a
    /// user-visible name, and neither must a truncated fragment of a sentence — a real
    /// posting whose opening line was corporate boilerplate ("JT4, LLC provides engineering
    /// and technical support to multiple western test ranges...") used to be blindly
    /// character-truncated into exactly that kind of fragment. A detected role title is
    /// preferred wherever the text offers one — a labeled "Title:"/"Position:" line, or a
    /// lead-in phrase such as "hiring a..." (checked with <see cref="SeniorityClassifier"/>
    /// help via <see cref="TryExtractTitlePhrase"/>) — searched across every line, not just
    /// the first. Only when nothing title-like exists anywhere does this fall back to the
    /// first line itself, and only when that line is short enough to plausibly already be a
    /// title rather than a chopped sentence; failing that it falls back to a recognized
    /// company name, and finally to a generic label — never to a truncated prose fragment.
    /// </summary>
    private static string DeriveNameFromRawText(string rawText)
    {
        var lines = rawText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
        {
            // RawText is required and CreateJob rejects a whitespace-only value, so this is
            // reachable only for a degenerate posting; still, never fall back to the job id.
            return GenericFallbackName;
        }

        foreach (var line in lines)
        {
            if (TryExtractLabeledTitle(line, out var labeled))
            {
                return Truncate(labeled);
            }
        }

        foreach (var line in lines)
        {
            if (TryExtractTitlePhrase(line, out var phrased))
            {
                return Truncate(phrased);
            }
        }

        var first = lines[0];
        var trimmed = first[^1] is '.' or '!' or '?' ? first[..^1].TrimEnd() : first;

        if (LooksLikeShortLabel(trimmed))
        {
            return Truncate(trimmed);
        }

        // The first line reads as prose, not a title (too long, too many words, or already
        // cut off with an ellipsis) — never surface a chopped sentence as the resume's name.
        return TryExtractCompanyName(first, out var company)
            ? Truncate($"{company} - {GenericFallbackName}")
            : GenericFallbackName;
    }

    /// <summary>
    /// Matches a line of the form <c>"Title: X"</c> (also "Job Title", "Position", "Role"),
    /// common in structured postings. Trusted without the noun/seniority sanity check
    /// <see cref="TryExtractTitlePhrase"/> applies, since an explicit label is unambiguous.
    /// </summary>
    private static bool TryExtractLabeledTitle(string line, out string title)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0)
        {
            var label = line[..colonIndex].Trim();
            var candidate = line[(colonIndex + 1)..].Trim();

            if (candidate.Length > 0 && TitleLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                title = candidate;
                return true;
            }
        }

        title = string.Empty;
        return false;
    }

    /// <summary>
    /// A first-line fallback is only accepted when it plausibly already is a short title —
    /// long lines, ellipsis-truncated lines (a common upstream scraper artifact), and
    /// multi-clause prose are always rejected in favor of a safer fallback further down
    /// <see cref="DeriveNameFromRawText"/>.
    /// </summary>
    private static bool LooksLikeShortLabel(string line) =>
        line.Length > 0 &&
        line.Length <= MaxDerivedNameLength &&
        !line.Contains('…', StringComparison.Ordinal) &&
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxFallbackLineWords;

    /// <summary>
    /// Looks for a leading proper-noun run ending in a recognized legal-entity suffix (e.g.
    /// <c>"JT4, LLC"</c>), the most common way a posting's opening sentence names the
    /// employer even when it never states a role title at all.
    /// </summary>
    private static bool TryExtractCompanyName(string line, out string company)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var take = 1; take <= Math.Min(4, words.Length); take++)
        {
            var lastWord = words[take - 1].TrimEnd(',');
            if (!CompanySuffixes.Contains(lastWord))
            {
                continue;
            }

            var precedingWordsAreProperNouns = true;
            for (var i = 0; i < take - 1; i++)
            {
                var word = words[i].TrimEnd(',');
                if (word.Length == 0 || !char.IsUpper(word[0]))
                {
                    precedingWordsAreProperNouns = false;
                    break;
                }
            }

            if (!precedingWordsAreProperNouns)
            {
                continue;
            }

            company = string.Join(' ', words.Take(take)).TrimEnd(',');
            return true;
        }

        company = string.Empty;
        return false;
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

    /// <summary>
    /// Truncates to <see cref="MaxDerivedNameLength"/> at a word boundary rather than a raw
    /// character index, so a name that does need shortening is cut between words, never
    /// mid-word.
    /// </summary>
    private static string Truncate(string name)
    {
        if (name.Length <= MaxDerivedNameLength)
        {
            return name;
        }

        var cut = name[..(MaxDerivedNameLength - 1)];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd() + "…";
    }
}
