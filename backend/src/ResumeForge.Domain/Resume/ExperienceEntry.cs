namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single job in the experience section of a resume.
/// </summary>
public sealed record ExperienceEntry
{
    /// <summary>The entry's id, e.g. <c>"exp:acme-corp"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Job title held.</summary>
    public required string Role { get; init; }

    /// <summary>Employer name.</summary>
    public required string Organization { get; init; }

    /// <summary>Free-form work location.</summary>
    public string? Location { get; init; }

    /// <summary>Date the role started.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Date the role ended, or null if it is current.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Accomplishment bullets for this role.</summary>
    public required IReadOnlyList<Bullet> Bullets { get; init; }

    /// <summary>Technologies used in this role.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>Whether this entry is included in the rendered resume.</summary>
    public bool Included { get; init; } = true;
}
