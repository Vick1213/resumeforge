namespace ResumeForge.Api.Contracts;

/// <summary>
/// Request body for <c>POST /api/tailor</c>, declared verbatim in CONTRACTS.md §9.
/// </summary>
public sealed record TailorRequest
{
    /// <summary>The job posting to tailor against.</summary>
    public required string JobId { get; init; }

    /// <summary>The base resume to start from, or null for the current base resume.</summary>
    public string? BaseResumeId { get; init; }

    /// <summary>Maximum number of model-generated rewrites accepted in this run.</summary>
    public int MaxRewrites { get; init; } = 6;

    /// <summary>When true, runs the pipeline and returns its trace without persisting anything.</summary>
    public bool DryRun { get; init; }
}
