namespace ResumeForge.Infrastructure.Jobs;

/// <summary>Structured metadata recovered from a job posting page's markup, when present.</summary>
public sealed record JobPostingMetadata
{
    /// <summary>Hiring company, from JSON-LD <c>hiringOrganization</c> or the <c>og:site_name</c> meta tag.</summary>
    public string? Company { get; init; }

    /// <summary>Job title, from JSON-LD <c>title</c> or the <c>og:title</c> meta tag.</summary>
    public string? Title { get; init; }

    /// <summary>Job location, from JSON-LD <c>jobLocation</c>.</summary>
    public string? Location { get; init; }
}
