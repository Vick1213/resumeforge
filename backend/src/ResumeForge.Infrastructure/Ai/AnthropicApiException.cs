namespace ResumeForge.Infrastructure.Ai;

/// <summary>Thrown when the Anthropic API returns a non-2xx response.</summary>
public sealed class AnthropicApiException : Exception
{
    /// <summary>Creates an exception describing a non-2xx Anthropic API response.</summary>
    public AnthropicApiException(int statusCode, string responseBody)
        : base($"Anthropic API returned HTTP {statusCode}: {Truncate(responseBody)}")
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
