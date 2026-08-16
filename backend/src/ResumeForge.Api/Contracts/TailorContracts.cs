using ResumeForge.Application.Tailoring;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// Request body for <c>POST /api/tailor</c>, declared verbatim in CONTRACTS.md §9.
/// </summary>
public sealed record TailorRequest
{
    /// <summary>The job posting to tailor against.</summary>
    public required string JobId { get; init; }

    /// <summary>The base resume to start from, or null for the current base resume.</summary>
    public string? BaseResumeId { get; init; }

    /// <summary>
    /// How much decision-making budget this run may spend the model on (CONTRACTS.md §6).
    /// Defaults to <see cref="ModelEffort.Standard"/> — an omitted effort must reproduce
    /// the exact pre-effort behaviour.
    /// </summary>
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;

    /// <summary>
    /// Overrides <see cref="TailorOptions.MaxRewrites"/> for this run only; null derives it
    /// from <see cref="Effort"/>. An explicit value always wins over the effort preset.
    /// </summary>
    public int? MaxRewrites { get; init; }

    /// <summary>When true, runs the pipeline and returns its trace without persisting anything.</summary>
    public bool DryRun { get; init; }
}
