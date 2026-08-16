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

    /// <summary>
    /// The rendered page budget for a tailoring run (CONTRACTS.md §6 "Page budget"). While
    /// the rendered document exceeds this many pages, <see cref="IPageBudgetEnforcer"/>
    /// excludes the single lowest-scoring still-included entry and re-renders. Defaults to
    /// <c>2</c>; <c>null</c> disables trimming entirely.
    /// </summary>
    public int? MaxPages { get; init; } = 2;

    /// <summary>
    /// Hard ceiling on the number of render-and-cut passes <see cref="IPageBudgetEnforcer"/>
    /// will perform for one run, regardless of how many cuttable entries the document has.
    /// Each pass removes exactly one entry, so the loop is already bounded by the number of
    /// cuttable entries in the document; this is a second, explicit ceiling so an
    /// adversarially large knowledge base can never force an unbounded number of renders.
    /// Fifty passes comfortably covers any realistic resume (far more entries than a page
    /// budget would ever need to cut) while keeping a pathological input's worst case cheap.
    /// </summary>
    public int MaxPageBudgetPasses { get; init; } = 50;
}
