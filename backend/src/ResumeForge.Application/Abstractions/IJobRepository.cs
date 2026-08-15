using ResumeForge.Application.Analysis;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to job posting and job analysis persistence.
/// </summary>
public interface IJobRepository
{
    /// <summary>Retrieves a job posting by id, or null when it does not exist.</summary>
    Task<JobPosting?> GetAsync(string id, CancellationToken ct);

    /// <summary>Inserts or updates <paramref name="posting"/>.</summary>
    Task SaveAsync(JobPosting posting, CancellationToken ct);

    /// <summary>Retrieves the stored analysis for job <paramref name="jobId"/>, if any.</summary>
    Task<JobAnalysis?> GetAnalysisAsync(string jobId, CancellationToken ct);

    /// <summary>Inserts or updates <paramref name="analysis"/> for its owning job.</summary>
    Task SaveAnalysisAsync(JobAnalysis analysis, CancellationToken ct);
}
