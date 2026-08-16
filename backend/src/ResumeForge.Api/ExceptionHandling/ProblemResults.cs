using Microsoft.AspNetCore.Http.HttpResults;

namespace ResumeForge.Api.ExceptionHandling;

/// <summary>
/// Factory methods for the RFC 9457 <c>ProblemDetails</c> responses endpoints return
/// directly (as opposed to the ones <see cref="ProblemDetailsExceptionHandler"/> produces
/// from an uncaught exception), so every hand-built error response shares the same
/// <c>type</c>/<c>title</c> vocabulary.
/// </summary>
internal static class ProblemResults
{
    /// <summary>A 400 Bad Request problem response.</summary>
    public static ProblemHttpResult BadRequest(string detail) => TypedResults.Problem(
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad request",
        type: ProblemTypes.ForStatus(StatusCodes.Status400BadRequest));

    /// <summary>A 404 Not Found problem response.</summary>
    public static ProblemHttpResult NotFound(string detail) => TypedResults.Problem(
        detail: detail,
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found",
        type: ProblemTypes.ForStatus(StatusCodes.Status404NotFound));

    /// <summary>A 501 Not Implemented problem response.</summary>
    public static ProblemHttpResult NotImplemented(string detail) => TypedResults.Problem(
        detail: detail,
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Not implemented",
        type: ProblemTypes.ForStatus(StatusCodes.Status501NotImplemented));
}
