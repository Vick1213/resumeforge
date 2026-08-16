namespace ResumeForge.Api.ExceptionHandling;

/// <summary>
/// Shared RFC 9457 <c>type</c>/<c>title</c> pairs for hand-built <c>ProblemDetails</c>
/// responses, so every error path in the API — whether raised by an explicit check or
/// caught by <see cref="ProblemDetailsExceptionHandler"/> — uses the same vocabulary.
/// </summary>
internal static class ProblemTypes
{
    /// <summary>Builds the conventional <c>https://httpstatuses.io/{status}</c> type URI for a status code.</summary>
    public static string ForStatus(int statusCode) => $"https://httpstatuses.io/{statusCode}";
}
