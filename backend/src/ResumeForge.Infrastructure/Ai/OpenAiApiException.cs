namespace ResumeForge.Infrastructure.Ai;

/// <summary>Thrown when an OpenAI-compatible endpoint returns a non-2xx response that isn't a strategy rejection.</summary>
public sealed class OpenAiApiException : Exception
{
    /// <summary>Creates an exception describing a non-2xx OpenAI-compatible API response.</summary>
    public OpenAiApiException(int statusCode, string responseBody)
        : base($"OpenAI-compatible API returned HTTP {statusCode}: {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body returned, for diagnostics.</summary>
    public string ResponseBody { get; }

    private static string Truncate(string body) => body.Length > 500 ? string.Concat(body.AsSpan(0, 500), "…") : body;
}
