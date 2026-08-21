using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Graph;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>The full output of one <see cref="ITailoringService"/> run.</summary>
public sealed record TailoringResult
{
    /// <summary>The tailored resume.</summary>
    public required ResumeDocument Document { get; init; }

    /// <summary>Every change made to produce <see cref="Document"/> from the base resume.</summary>
    public required IReadOnlyList<ResumeDiffEntry> Diff { get; init; }

    /// <summary>The accepted and rejected commands the model proposed.</summary>
    public required CommandValidationResult Commands { get; init; }

    /// <summary>How well the tailored resume covers the job's requirements.</summary>
    public required CoverageReport Coverage { get; init; }

    /// <summary>
    /// The <c>ats-review</c> pass's verdict on the <em>base</em> resume: what a screener
    /// found missing, and the score before and after closing those gaps. Null when the pass
    /// did not run (<see cref="ModelEffort.Minimal"/>) or could not complete — its absence
    /// never fails a run.
    /// </summary>
    /// <remarks>
    /// Reported alongside <see cref="Coverage"/> rather than instead of it, because the two
    /// answer different questions. Coverage is deterministic and mechanical: it asks whether
    /// some included node carries a matching skill tag, and it is computed against the
    /// finished document. This is a model's judgement, computed against the document the run
    /// started from, and it is the only place the product says what the resume was missing
    /// and what closing those gaps would be worth.
    /// </remarks>
    public AtsReview? AtsReview { get; init; }

    /// <summary>Total token spend for the run.</summary>
    public required TokenUsage Usage { get; init; }

    /// <summary>The graph execution trace, for the live pipeline view.</summary>
    public required IReadOnlyList<GraphNodeTrace> Trace { get; init; }

    /// <summary>
    /// The final rendered page count of <see cref="Document"/>, after page-budget
    /// enforcement (CONTRACTS.md §6 "Page budget").
    /// </summary>
    public required int PageCount { get; init; }

    /// <summary>
    /// True when <see cref="Document"/> fits <see cref="TailorOptions.MaxPages"/> (or the
    /// budget was disabled). False when the floor — basics and the single highest-scoring
    /// experience entry — was reached before the page count came within budget.
    /// </summary>
    public required bool FitsBudget { get; init; }
}
