using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/resumes</c> routes (CONTRACTS.md §9).</summary>
public static class ResumeEndpoints
{
    /// <summary>Registers the resume routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/resumes").WithTags("Resumes");

        group.MapGet("/", ListAsync)
            .WithName("ListResumes")
            .Produces<IReadOnlyList<ResumeSummaryDto>>();

        group.MapGet("/{id}", GetAsync)
            .WithName("GetResume")
            .Produces<ResumeDocument>();

        group.MapPost("/base", RebuildBaseAsync)
            .WithName("RebuildBaseResume")
            .Produces<ResumeDocument>();

        return app;
    }

    private static async Task<IResult> ListAsync(IResumeRepository repository, CancellationToken ct)
    {
        var resumes = await repository.ListAsync(ct).ConfigureAwait(false);
        var baseResume = await repository.GetBaseAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ResumeSummaryDto>>([.. resumes.Select(r => ToSummary(r, baseResume?.Id))]);
    }

    private static async Task<IResult> GetAsync(string id, IResumeRepository repository, CancellationToken ct)
    {
        var resume = await repository.GetAsync(id, ct).ConfigureAwait(false);

        return resume is null
            ? ProblemResults.NotFound($"No resume with id '{id}' was found.")
            : TypedResults.Ok(resume);
    }

    private static async Task<IResult> RebuildBaseAsync(
        IKnowledgeBaseReader knowledgeBaseReader, IResumeBuilder resumeBuilder, IResumeRepository resumeRepository, CancellationToken ct)
    {
        var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);

        // Reuse the existing base resume's id, when there is one, so this rebuilds the same
        // row in place rather than creating a second base resume. The row is identified by
        // ResumeEntity.IsBase (an explicit, persisted flag), not by name, so this holds even
        // if the base resume has since been renamed.
        var existingBase = await resumeRepository.GetBaseAsync(ct).ConfigureAwait(false);

        var rebuilt = resumeBuilder.Build(snapshot, existingBase?.Id);
        await resumeRepository.SaveAsync(rebuilt, isBase: true, ct).ConfigureAwait(false);

        return TypedResults.Ok(rebuilt);
    }

    private static ResumeSummaryDto ToSummary(ResumeDocument resume, string? baseResumeId) => new()
    {
        Id = resume.Id,
        Name = resume.Name,
        // Compares against the id GetBaseAsync (a real IsBase column lookup) returned,
        // never the resume's Name — a rename can never change which resume this reports as
        // the base.
        IsBase = resume.Id == baseResumeId,
        UpdatedAt = resume.UpdatedAt,
    };
}
