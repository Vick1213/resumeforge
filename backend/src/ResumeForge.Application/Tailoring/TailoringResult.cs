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
}
