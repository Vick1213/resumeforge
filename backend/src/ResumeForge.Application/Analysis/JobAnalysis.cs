namespace ResumeForge.Application.Analysis;

/// <summary>
/// The deterministic, model-free analysis of a <see cref="JobPosting"/>: its
/// requirements, ranked keywords, and inferred seniority.
/// </summary>
public sealed record JobAnalysis
{
    /// <summary>The job posting this analysis was produced from.</summary>
    public required string JobId { get; init; }

    /// <summary>Requirements extracted from the posting, in document order.</summary>
    public required IReadOnlyList<Requirement> Requirements { get; init; }

    /// <summary>Normalized, ranked keywords extracted from the posting (capped at 40).</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Canonical skills mentioned in the posting that are recognized by the skill
    /// taxonomy — the subset of <see cref="Keywords"/> considered a confident match.
    /// </summary>
    public required IReadOnlyList<string> MatchedSkills { get; init; }

    /// <summary>
    /// Skill-like terms mentioned in the posting's requirements that the skill taxonomy
    /// does not recognize.
    /// </summary>
    public required IReadOnlyList<string> MissingSkills { get; init; }

    /// <summary>Seniority inferred from the title and years-of-experience phrasing.</summary>
    public required SeniorityLevel Seniority { get; init; }
}

/// <summary>Seniority tiers a job posting can be classified into, from least to most senior.</summary>
public enum SeniorityLevel
{
    /// <summary>Could not be determined.</summary>
    Unknown,

    /// <summary>Internship.</summary>
    Intern,

    /// <summary>Junior / entry-level.</summary>
    Junior,

    /// <summary>Mid-level.</summary>
    Mid,

    /// <summary>Senior.</summary>
    Senior,

    /// <summary>Staff.</summary>
    Staff,

    /// <summary>Principal.</summary>
    Principal,
}

/// <summary>A single requirement or responsibility extracted from a job posting.</summary>
public sealed record Requirement
{
    /// <summary>The requirement's id, e.g. <c>"req:0"</c>, stable within one analysis.</summary>
    public required string Id { get; init; }

    /// <summary>The requirement text as it appeared in the posting.</summary>
    public required string Text { get; init; }

    /// <summary>The kind of requirement.</summary>
    public required RequirementKind Kind { get; init; }

    /// <summary>True when phrased or positioned as required, false when optional/preferred.</summary>
    public required bool IsMandatory { get; init; }

    /// <summary>Canonical skills this requirement mentions.</summary>
    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>Relative importance of this requirement, 0..1.</summary>
    public double Weight { get; init; }
}

/// <summary>The category a <see cref="Requirement"/> falls into.</summary>
public enum RequirementKind
{
    /// <summary>A specific technology or skill proficiency.</summary>
    Skill,

    /// <summary>A years-of-experience or background requirement.</summary>
    Experience,

    /// <summary>A degree or academic credential requirement.</summary>
    Education,

    /// <summary>A duty the role performs, rather than a qualification.</summary>
    Responsibility,

    /// <summary>Does not fit another category.</summary>
    Other,
}
