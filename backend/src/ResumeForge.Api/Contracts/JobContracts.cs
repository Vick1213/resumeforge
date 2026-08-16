namespace ResumeForge.Api.Contracts;

/// <summary>
/// Request body for <c>POST /api/jobs</c>, declared inline in CONTRACTS.md §9's route
/// table (<c>CreateJobRequest {{ url?, rawText? }}</c>). Exactly one of <see cref="Url"/>
/// or <see cref="RawText"/> must be supplied.
/// </summary>
public sealed record CreateJobRequest
{
    /// <summary>A URL to fetch the posting from. Mutually exclusive with <see cref="RawText"/>.</summary>
    public string? Url { get; init; }

    /// <summary>Pasted posting text. Mutually exclusive with <see cref="Url"/>.</summary>
    public string? RawText { get; init; }
}
