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

    /// <summary>
    /// The effort level this run was configured for (CONTRACTS.md §6). Governs which
    /// additional ops are available — currently just <c>injectKeywords</c>, gated at
    /// <see cref="ModelEffort.Thorough"/> and above by <see cref="CommandValidator"/>.
    /// <see cref="MaxRewrites"/> is ordinarily derived from this via
    /// <see cref="ModelEffortExtensions.ResolveMaxRewrites"/>, but stays independently
    /// settable here since an explicit override always wins.
    /// </summary>
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;
}
