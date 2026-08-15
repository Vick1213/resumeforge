namespace ResumeForge.Infrastructure.Jobs;

/// <summary>Thrown when a job posting could not be fetched or contained no readable text.</summary>
public sealed class JobPostingFetchException : Exception
{
    /// <summary>Creates an exception describing why fetching <paramref name="url"/> failed.</summary>
    public JobPostingFetchException(string url, string reason, Exception? innerException = null)
        : base($"Failed to fetch job posting from '{url}': {reason}", innerException)
    {
        Url = url;
    }

    /// <summary>The URL that failed to fetch.</summary>
    public string Url { get; }
}
