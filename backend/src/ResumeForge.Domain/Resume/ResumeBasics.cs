namespace ResumeForge.Domain.Resume;

/// <summary>
/// Contact and identity information shown at the top of a resume.
/// </summary>
public sealed record ResumeBasics
{
    /// <summary>The candidate's full display name.</summary>
    public required string FullName { get; init; }

    /// <summary>A short professional headline, e.g. "Senior Backend Engineer".</summary>
    public string? Headline { get; init; }

    /// <summary>Contact email address.</summary>
    public string? Email { get; init; }

    /// <summary>Contact phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>Free-form location, e.g. "Seattle, WA".</summary>
    public string? Location { get; init; }

    /// <summary>Personal website URL.</summary>
    public string? Website { get; init; }

    /// <summary>Full LinkedIn profile URL.</summary>
    public string? LinkedIn { get; init; }

    /// <summary>Full GitHub profile URL.</summary>
    public string? GitHub { get; init; }
}
