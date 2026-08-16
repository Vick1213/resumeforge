using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ResumeForge.Application.Graph;
using ResumeForge.Infrastructure.GitHub;
using ResumeForge.Infrastructure.Jobs;

namespace ResumeForge.Api.ExceptionHandling;

/// <summary>
/// Catches every exception that escapes an endpoint and converts it into an RFC 9457
/// <c>ProblemDetails</c> response, so no raw exception ever reaches a client as an HTML
/// error page. <see cref="Classify"/> unwraps <see cref="GraphExecutionException"/> (thrown
/// when a <c>.Critical()</c> tailoring-graph node fails) to the status its inner exception
/// would have mapped to on its own.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = Classify(exception);

        logger.LogError(exception, "Request failed with HTTP {StatusCode}.", statusCode);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Type = ProblemTypes.ForStatus(statusCode),
                Detail = exception.Message,
            },
        }).ConfigureAwait(false);
    }

    private static (int StatusCode, string Title) Classify(Exception exception) => exception switch
    {
        GraphExecutionException { InnerException: { } inner } => Classify(inner),
        NotSupportedException => (StatusCodes.Status501NotImplemented, "Not implemented"),
        JobPostingFetchException => (StatusCodes.Status502BadGateway, "Upstream fetch failed"),
        GitHubApiException => (StatusCodes.Status502BadGateway, "GitHub API error"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
        FormatException or ArgumentException => (StatusCodes.Status400BadRequest, "Bad request"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
    };
}
