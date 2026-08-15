namespace ResumeForge.Application.Tailoring;

/// <summary>How well a tailored resume evidences the job's requirements.</summary>
public sealed record CoverageReport
{
    /// <summary>Covered mandatory requirements ÷ total mandatory requirements; 1.0 when there are none.</summary>
    public required double Score { get; init; }

    /// <summary>Per-requirement coverage detail.</summary>
    public required IReadOnlyList<RequirementCoverage> Requirements { get; init; }
}

/// <summary>Whether, and by what, a single requirement is evidenced in the tailored resume.</summary>
public sealed record RequirementCoverage
{
    /// <summary>The requirement's id.</summary>
    public required string RequirementId { get; init; }

    /// <summary>The requirement's text, for display without a second lookup.</summary>
    public required string RequirementText { get; init; }

    /// <summary>True when at least one included node in the document evidences this requirement.</summary>
    public required bool Covered { get; init; }

    /// <summary>Ids of the included nodes that evidence this requirement.</summary>
    public required IReadOnlyList<string> EvidenceIds { get; init; }
}
