using ResumeForge.Application.Abstractions;

namespace ResumeForge.Application.Graph;

/// <summary>The outcome of one full <see cref="GraphExecutor.RunAsync"/> call.</summary>
public sealed record GraphRunResult
{
    /// <summary>Every node's terminal status, duration, and token spend.</summary>
    public required IReadOnlyList<GraphNodeTrace> Trace { get; init; }

    /// <summary>True when no node in the run <see cref="GraphNodeStatus.Failed"/> or <see cref="GraphNodeStatus.Cancelled"/>.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Every node's raw result, keyed by node name. A failed node has no entry's worth of
    /// data beyond null; a skipped node's value is null.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Outputs { get; init; }

    /// <summary>The run's total token spend across every node.</summary>
    public required TokenUsage Usage { get; init; }
}
