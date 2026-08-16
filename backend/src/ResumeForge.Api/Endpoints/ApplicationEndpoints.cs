using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/applications</c> routes (CONTRACTS.md §9).</summary>
public static class ApplicationEndpoints
{
    /// <summary>Registers the application-tracker routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/applications").WithTags("Applications");

        group.MapGet("/", ListAsync)
            .WithName("ListApplications")
            .Produces<IReadOnlyList<ApplicationDto>>();

        group.MapPost("/", CreateAsync)
            .WithName("CreateApplication")
            .Produces<ApplicationDto>(StatusCodes.Status201Created);

        group.MapPatch("/{id}", UpdateAsync)
            .WithName("UpdateApplication")
            .Produces<ApplicationDto>();

        return app;
    }

    private static async Task<IResult> ListAsync(IApplicationRepository applications, IJobRepository jobs, CancellationToken ct)
    {
        var records = await applications.ListAsync(ct).ConfigureAwait(false);
        var dtos = new List<ApplicationDto>(records.Count);

        foreach (var record in records)
        {
            var job = await jobs.GetAsync(record.JobId, ct).ConfigureAwait(false);
            dtos.Add(ToDto(record, job));
        }

        return TypedResults.Ok<IReadOnlyList<ApplicationDto>>(dtos);
    }

    private static async Task<IResult> CreateAsync(
        CreateApplicationRequest request,
        IApplicationRepository applications,
        IJobRepository jobs,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var job = await jobs.GetAsync(request.JobId, ct).ConfigureAwait(false);
        if (job is null)
        {
            return ProblemResults.NotFound($"No job posting with id '{request.JobId}' was found.");
        }

        // CreateApplicationRequest carries company/title/jobUrl/location because
        // JobApplicationRecord has no columns for them; JobPosting does, so they are folded
        // back onto the associated posting instead of being dropped. See ApplicationDto's
        // remarks for the fields (coverageScore, usage) that have nowhere to go at all.
        var updatedJob = job with
        {
            Company = request.Company,
            Title = request.Title,
            SourceUrl = request.JobUrl ?? job.SourceUrl,
            Location = request.Location ?? job.Location,
        };
        await jobs.SaveAsync(updatedJob, ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var created = await applications.CreateAsync(
            new JobApplicationRecord
            {
                Id = string.Empty,
                JobId = request.JobId,
                ResumeId = request.ResumeId,
                Status = request.Status,
                Notes = request.Notes,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct).ConfigureAwait(false);

        return TypedResults.Json(ToDto(created, updatedJob), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        UpdateApplicationRequest request,
        IApplicationRepository applications,
        IJobRepository jobs,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var existing = await applications.GetAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return ProblemResults.NotFound($"No application with id '{id}' was found.");
        }

        // Every property is optional and a null value means "leave unchanged" — plain
        // nullable properties cannot distinguish an omitted field from an explicit null, so
        // this cannot clear Notes back to null once set. See the implementation report.
        var updated = existing with
        {
            Status = request.Status ?? existing.Status,
            Notes = request.Notes ?? existing.Notes,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        var saved = await applications.UpdateAsync(updated, ct).ConfigureAwait(false);
        if (saved is null)
        {
            return ProblemResults.NotFound($"No application with id '{id}' was found.");
        }

        var job = await jobs.GetAsync(saved.JobId, ct).ConfigureAwait(false);
        return TypedResults.Ok(ToDto(saved, job));
    }

    private static ApplicationDto ToDto(JobApplicationRecord record, JobPosting? job) => new()
    {
        Id = record.Id,
        JobId = record.JobId,
        ResumeId = record.ResumeId,
        Company = job?.Company ?? string.Empty,
        Title = job?.Title ?? string.Empty,
        Status = record.Status,
        JobUrl = job?.SourceUrl,
        Location = job?.Location,
        Notes = record.Notes,
        // No port exists to look up the tailoring run that produced record.ResumeId (or to
        // enumerate all runs), so these have nowhere to be read from. AppliedAt has nowhere
        // to be persisted at all. See the implementation report.
        CoverageScore = null,
        Usage = null,
        AppliedAt = null,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };
}
