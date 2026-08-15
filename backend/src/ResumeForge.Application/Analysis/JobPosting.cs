namespace ResumeForge.Application.Analysis;

/// <summary>
/// A job posting fetched from a URL or supplied as raw text, prior to any analysis.
/// </summary>
public sealed record JobPosting
{
    /// <summary>The posting's own id.</summary>
    public required string Id { get; init; }

    /// <summary>The URL the posting was fetched from, or a synthetic marker for pasted text.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>Hiring company, when it could be determined.</summary>
    public string? Company { get; init; }

    /// <summary>Job title, when it could be determined.</summary>
    public string? Title { get; init; }

    /// <summary>Job location, when it could be determined.</summary>
    public string? Location { get; init; }

    /// <summary>The full, unprocessed posting text.</summary>
    public required string RawText { get; init; }

    /// <summary>When the posting was fetched.</summary>
    public DateTimeOffset FetchedAt { get; init; }
}
