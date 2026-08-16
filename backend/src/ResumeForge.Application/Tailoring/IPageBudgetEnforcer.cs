using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic, model-free enforcement of <see cref="TailorOptions.MaxPages"/>
/// (CONTRACTS.md §6 "Page budget"). Runs after commands are executed and before the final
/// render: renders the document, counts pages, and while over budget excludes the single
/// lowest-scoring still-included entry and renders again.
/// </summary>
public interface IPageBudgetEnforcer
{
    /// <summary>
    /// Trims <paramref name="document"/> to fit <see cref="TailorOptions.MaxPages"/>, using
    /// <paramref name="candidates"/> to rank entries by relevance. Never touches basics or
    /// the single highest-scoring experience entry. <paramref name="diff"/> is the diff
    /// produced so far (from command execution); every entry this enforcer excludes is
    /// appended to it as a <see cref="DiffKind.Excluded"/> entry naming the budget.
    /// </summary>
    Task<PageBudgetResult> EnforceAsync(
        ResumeDocument document,
        IReadOnlyList<ResumeDiffEntry> diff,
        CandidateSet candidates,
        TailorOptions options,
        CancellationToken ct);
}

/// <summary>The result of one <see cref="IPageBudgetEnforcer.EnforceAsync"/> call.</summary>
public sealed record PageBudgetResult
{
    /// <summary>The document after any budget-driven exclusions.</summary>
    public required ResumeDocument Document { get; init; }

    /// <summary>The input diff, plus one <see cref="DiffKind.Excluded"/> entry per budget cut.</summary>
    public required IReadOnlyList<ResumeDiffEntry> Diff { get; init; }

    /// <summary>The final rendered page count of <see cref="Document"/>.</summary>
    public required int PageCount { get; init; }

    /// <summary>
    /// True when <see cref="Document"/> fits the budget, or the budget was disabled
    /// (<see cref="TailorOptions.MaxPages"/> is null). False when the floor was reached —
    /// only basics and the single highest-scoring experience entry remain among the
    /// cuttable kinds — before the page count came within budget.
    /// </summary>
    public required bool FitsBudget { get; init; }
}
