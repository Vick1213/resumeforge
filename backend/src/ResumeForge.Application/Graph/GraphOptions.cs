namespace ResumeForge.Application.Graph;

/// <summary>Tunable knobs for <see cref="GraphExecutor"/>.</summary>
public sealed class GraphOptions
{
    /// <summary>
    /// The maximum number of nodes the executor runs at once. Defaults to
    /// <c>Environment.ProcessorCount - 1</c>, floored at 1.
    /// </summary>
    public int MaxConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);
}
