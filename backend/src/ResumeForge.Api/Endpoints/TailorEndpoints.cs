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
        var tailoredName = job.Company is { Length: > 0 } company && job.Title is { Length: > 0 } title
            ? $"{company} - {title}"
            : job.Title ?? job.Company ?? $"Tailored - {job.Id}";

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
}
