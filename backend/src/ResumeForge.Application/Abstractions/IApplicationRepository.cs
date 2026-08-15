namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to job-application tracker persistence, behind <c>/api/applications</c>.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>Lists every tracked application, most recently updated first.</summary>
    Task<IReadOnlyList<JobApplicationRecord>> ListAsync(CancellationToken ct);

    /// <summary>Retrieves a tracked application by id, or null when it does not exist.</summary>
    Task<JobApplicationRecord?> GetAsync(string id, CancellationToken ct);

    /// <summary>Creates a new tracked application and returns it with its assigned id.</summary>
    Task<JobApplicationRecord> CreateAsync(JobApplicationRecord application, CancellationToken ct);

    /// <summary>
    /// Applies an update to the application identified by <see cref="JobApplicationRecord.Id"/>.
    /// Returns the updated record, or null when no application with that id exists.
    /// </summary>
    Task<JobApplicationRecord?> UpdateAsync(JobApplicationRecord application, CancellationToken ct);
}

/// <summary>The stage of a tracked job application in the funnel.</summary>
public enum ApplicationStatus
{
    /// <summary>Saved for later, not yet applied.</summary>
    Saved,

    /// <summary>Application submitted.</summary>
    Applied,

    /// <summary>In an interview loop.</summary>
    Interviewing,

    /// <summary>An offer has been extended.</summary>
    Offer,

    /// <summary>The application was rejected.</summary>
    Rejected,

    /// <summary>The candidate withdrew.</summary>
    Withdrawn,
}

/// <summary>A single row in the job-application tracker.</summary>
public sealed record JobApplicationRecord
{
    /// <summary>The record's own id.</summary>
    public required string Id { get; init; }

    /// <summary>The job posting this application is for.</summary>
    public required string JobId { get; init; }

    /// <summary>The resume submitted, if any.</summary>
    public string? ResumeId { get; init; }

    /// <summary>Current funnel stage.</summary>
    public required ApplicationStatus Status { get; init; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; init; }

    /// <summary>When this application was first tracked.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this application was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
