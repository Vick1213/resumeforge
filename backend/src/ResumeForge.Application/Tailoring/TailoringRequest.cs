namespace ResumeForge.Application.Tailoring;

/// <summary>Input to one <see cref="ITailoringService"/> run.</summary>
public sealed record TailoringRequest
{
    /// <summary>The job posting to tailor against. Must already exist in <see cref="Abstractions.IJobRepository"/>.</summary>
    public required string JobId { get; init; }

    /// <summary>The base resume to start from, or null for the current base resume.</summary>
    public string? BaseResumeId { get; init; }

    /// <summary>Overrides <see cref="TailorOptions.MaxRewrites"/> for this run only.</summary>
    public int? MaxRewrites { get; init; }

    /// <summary>
    /// How much decision-making budget this run may spend the model on (CONTRACTS.md §6).
    /// Defaults to <see cref="ModelEffort.Standard"/>, which is what every pre-effort
    /// behaviour maps to — an omitted effort must reproduce the exact prior output.
    /// </summary>
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;

    /// <summary>
    /// Overrides <see cref="TailorOptions.MaxPages"/> for this run only (CONTRACTS.md §6
    /// "Page budget"). Defaults to <c>2</c>, matching <see cref="TailorOptions.MaxPages"/>'s
    /// own default; an explicit <c>null</c> disables page-budget trimming entirely.
    /// </summary>
    public int? MaxPages { get; init; } = 2;

    /// <summary>
    /// Entry ids (<c>exp:</c>, <c>prj:</c>, <c>edu:</c>, <c>cert:</c>) forced into the
    /// tailored resume regardless of the model's commands or the page-budget trimmer
    /// (CONTRACTS.md §6 "Forced inclusion"). Null or empty means no forcing.
    /// </summary>
    public IReadOnlyList<string>? PinnedEntryIds { get; init; }

    /// <summary>
    /// Entry ids forced out of the tailored resume regardless of the model's commands
    /// (CONTRACTS.md §6 "Forced inclusion"). Null or empty means no forcing. An id present
    /// in both this list and <see cref="PinnedEntryIds"/> is rejected by the endpoint before
    /// a run ever starts.
    /// </summary>
    public IReadOnlyList<string>? ExcludedEntryIds { get; init; }
}

/// <summary>
/// The result of re-checking every accepted rewrite against <see cref="IFabricationGuard"/>
/// after validation, produced by the tailoring graph's <c>verify-fabrication</c> node.
/// </summary>
public sealed record FabricationVerification
{
    /// <summary>True when every accepted rewrite still passes the anti-fabrication check.</summary>
    public required bool Passed { get; init; }

    /// <summary>Descriptions of any violation found, empty when <see cref="Passed"/> is true.</summary>
    public required IReadOnlyList<string> Violations { get; init; }
}
