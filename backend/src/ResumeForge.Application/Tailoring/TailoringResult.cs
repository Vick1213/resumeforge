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
