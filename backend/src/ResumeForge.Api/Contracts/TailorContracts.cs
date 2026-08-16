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

    /// <summary>
    /// Bounds the rendered result to this many pages (CONTRACTS.md §6 "Page budget").
    /// Defaults to <c>2</c>; an explicit <c>null</c> disables page-budget trimming entirely.
    /// </summary>
    public int? MaxPages { get; init; } = 2;

    /// <summary>When true, runs the pipeline and returns its trace without persisting anything.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Entry ids (<c>exp:</c>, <c>prj:</c>, <c>edu:</c>, <c>cert:</c> — the same ids
    /// CONTRACTS.md §6's command protocol targets and <c>GET /api/knowledge</c> returns)
    /// forced into the tailored resume, overriding both the model's include/exclude
    /// decisions and the page-budget trimmer (CONTRACTS.md §6 "Forced inclusion"). Null or
    /// empty means no forcing. An id must resolve to a real knowledge-base entry, or the
    /// request fails with 400.
    /// </summary>
    public IReadOnlyList<string>? PinnedEntryIds { get; init; }

    /// <summary>
    /// Entry ids forced out of the tailored resume, overriding the model's include/exclude
    /// decisions (CONTRACTS.md §6 "Forced inclusion"). Null or empty means no forcing. An id
    /// present in both this list and <see cref="PinnedEntryIds"/>, or one that does not
    /// resolve to a real knowledge-base entry, fails the request with 400.
    /// </summary>
    public IReadOnlyList<string>? ExcludedEntryIds { get; init; }
}
