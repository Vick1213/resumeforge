using ResumeForge.Application.Analysis;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port that retrieves a job posting's raw text from a URL. Implemented in Infrastructure
/// against an HTTP client with site-specific extraction.
/// </summary>
public interface IJobPostingFetcher
{
    /// <summary>Fetches and extracts the job posting at <paramref name="url"/>.</summary>
    Task<JobPosting> FetchAsync(string url, CancellationToken ct);
}
