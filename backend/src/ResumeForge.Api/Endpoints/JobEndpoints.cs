using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/jobs</c> routes (CONTRACTS.md §9).</summary>
public static class JobEndpoints
{
    /// <summary>Registers the job posting routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/jobs").WithTags("Jobs");

        group.MapPost("/", CreateAsync)
            .WithName("CreateJob")
            .Produces<JobPosting>(StatusCodes.Status201Created);

        group.MapGet("/{id}/analysis", GetAnalysisAsync)
            .WithName("GetJobAnalysis")
            .Produces<JobAnalysis>();

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateJobRequest request,
        IJobPostingFetcher fetcher,
        IJobRepository repository,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);
        var hasRawText = !string.IsNullOrWhiteSpace(request.RawText);

        if (hasUrl == hasRawText)
        {
            return ProblemResults.BadRequest("Exactly one of 'url' or 'rawText' must be supplied.");
        }

        var posting = hasUrl
            ? await fetcher.FetchAsync(request.Url!, ct).ConfigureAwait(false)
            : new JobPosting
            {
                Id = Guid.NewGuid().ToString(),
                SourceUrl = $"pasted:{Guid.NewGuid()}",
                RawText = request.RawText!,
                FetchedAt = timeProvider.GetUtcNow(),
            };

        await repository.SaveAsync(posting, ct).ConfigureAwait(false);

        // No GET /api/jobs/{id} route exists to point a Location header at (CONTRACTS.md
        // §9 only declares GET /api/jobs/{id}/analysis), so this returns 201 with the
        // created resource in the body and no Location header.
        return TypedResults.Json(posting, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAnalysisAsync(
        string id, IJobRepository repository, IJobAnalyzer analyzer, CancellationToken ct)
    {
        var posting = await repository.GetAsync(id, ct).ConfigureAwait(false);
        if (posting is null)
        {
            return ProblemResults.NotFound($"No job posting with id '{id}' was found.");
        }

        var analysis = await repository.GetAnalysisAsync(id, ct).ConfigureAwait(false);
        if (analysis is null)
        {
            // Deterministic and model-free (CONTRACTS.md §4), so it is computed on first
            // request and cached from then on rather than eagerly on POST /api/jobs.
            analysis = analyzer.Analyze(posting);
            await repository.SaveAnalysisAsync(analysis, ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(analysis);
    }
}
