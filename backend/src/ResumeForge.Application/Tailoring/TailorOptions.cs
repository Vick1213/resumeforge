namespace ResumeForge.Application.Tailoring;

/// <summary>Tunable limits for a single tailoring run.</summary>
public sealed record TailorOptions
{
    /// <summary>Maximum number of <see cref="RewriteCommand"/>s accepted in one run.</summary>
    public int MaxRewrites { get; init; } = 6;

    /// <summary>Maximum number of experience entries included in the base resume brief.</summary>
    public int MaxExperienceEntries { get; init; } = 8;

    /// <summary>Maximum number of project entries included in the base resume brief.</summary>
    public int MaxProjectEntries { get; init; } = 6;

    /// <summary>Maximum number of bullets per entry offered as candidates.</summary>
    public int MaxBulletsPerEntry { get; init; } = 6;

    /// <summary>Maximum number of scored candidates per section offered to the model.</summary>
    public int CandidateLimit { get; init; } = 40;
}
