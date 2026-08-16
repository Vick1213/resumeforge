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
