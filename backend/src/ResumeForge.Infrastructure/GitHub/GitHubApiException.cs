namespace ResumeForge.Infrastructure.GitHub;

/// <summary>Thrown when the GitHub REST API returns a non-2xx response.</summary>
public sealed class GitHubApiException : Exception
{
    /// <summary>Creates an exception describing a non-2xx GitHub API response.</summary>
    public GitHubApiException(string username, string repository, int statusCode, string responseBody)
        : base($"GitHub API returned HTTP {statusCode} for '{username}/{repository}': {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code returned.</summary>
    public int StatusCode { get; }

    private static string Truncate(string body) => body.Length > 500 ? string.Concat(body.AsSpan(0, 500), "…") : body;
}
