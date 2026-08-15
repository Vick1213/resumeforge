namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single project in the projects section of a resume.
/// </summary>
public sealed record ProjectEntry
{
    /// <summary>The entry's id, e.g. <c>"prj:graph-runner"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Project name.</summary>
    public required string Name { get; init; }

    /// <summary>One-line description of the project.</summary>
    public string? Tagline { get; init; }

    /// <summary>Live or demo URL.</summary>
    public string? Url { get; init; }

    /// <summary>Source repository URL.</summary>
    public string? RepoUrl { get; init; }

    /// <summary>Date work on the project started.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>Date work on the project ended, or null if it is ongoing.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Accomplishment bullets for this project.</summary>
    public required IReadOnlyList<Bullet> Bullets { get; init; }

    /// <summary>Technologies used in this project.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>Whether this entry is included in the rendered resume.</summary>
    public bool Included { get; init; } = true;
}
