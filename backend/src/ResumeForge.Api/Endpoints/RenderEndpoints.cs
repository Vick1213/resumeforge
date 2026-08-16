using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/render</c> routes (CONTRACTS.md §9).</summary>
public static class RenderEndpoints
{
    /// <summary>Registers the render route on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapRenderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/render/{resumeId}", RenderAsync)
            .WithTags("Render")
            .WithName("RenderResume")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");

        return app;
    }

    private static async Task<IResult> RenderAsync(
        string resumeId,
        RenderRequest? request,
        string? format,
        IResumeRepository resumeRepository,
        IResumeRenderer renderer,
        CancellationToken ct)
    {
        var resume = await resumeRepository.GetAsync(resumeId, ct).ConfigureAwait(false);
        if (resume is null)
        {
            return ProblemResults.NotFound($"No resume with id '{resumeId}' was found.");
        }

        // Accepts the format from either the JSON body (CONTRACTS.md §9's documented shape)
        // or a '?format=' query parameter, so an autofill document's DownloadUrl (built as
        // a plain absolute URL with no body) is actually usable.
        var requestedFormat = format ?? request?.Format;
        if (requestedFormat is null || !TryParseFormat(requestedFormat, out var parsedFormat))
        {
            return ProblemResults.BadRequest($"'{requestedFormat}' is not a supported render format (expected pdf, html, md, or docx).");
        }

        // RenderFormat.Docx throws NotSupportedException synchronously, before this awaits
        // anything; ProblemDetailsExceptionHandler maps that to HTTP 501 per CONTRACTS.md §9.
        var rendered = await renderer.RenderAsync(resume, parsedFormat, ct).ConfigureAwait(false);

        return TypedResults.File(rendered.Content, rendered.ContentType, rendered.FileName);
    }

    private static bool TryParseFormat(string format, out RenderFormat parsed)
    {
        switch (format.Trim().ToLowerInvariant())
        {
            case "pdf":
                parsed = RenderFormat.Pdf;
                return true;
            case "html":
                parsed = RenderFormat.Html;
                return true;
            // CONTRACTS.md §9's route table writes the wire value as "md"; "markdown" is
            // also accepted since RenderFormat's own member is named Markdown.
            case "md":
            case "markdown":
                parsed = RenderFormat.Markdown;
                return true;
            case "docx":
                parsed = RenderFormat.Docx;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
