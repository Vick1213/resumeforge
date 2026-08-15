namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single entry in the education section of a resume.
/// </summary>
public sealed record EducationEntry
{
    /// <summary>The entry's id, e.g. <c>"edu:uw-madison"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Institution name.</summary>
    public required string Institution { get; init; }

    /// <summary>Credential earned, e.g. "B.S. Computer Science".</summary>
    public required string Credential { get; init; }

    /// <summary>Date study started.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>Date study ended, or graduation date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Free-form institution location.</summary>
    public string? Location { get; init; }

    /// <summary>Grade point average, if disclosed.</summary>
    public double? Gpa { get; init; }

    /// <summary>Notable coursework, honors, or activities.</summary>
    public IReadOnlyList<string> Highlights { get; init; } = [];

    /// <summary>Whether this entry is included in the rendered resume.</summary>
    public bool Included { get; init; } = true;
}
