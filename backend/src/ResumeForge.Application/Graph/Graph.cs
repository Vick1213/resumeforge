namespace ResumeForge.Application.Graph;

/// <summary>
/// A validated, immutable DAG produced by <see cref="GraphBuilder.Build"/>: every
/// dependency name is known to exist and the edges contain no cycle. Hand this to
/// <see cref="GraphExecutor.RunAsync"/> to execute it.
/// </summary>
public sealed class Graph
{
    internal Graph(IReadOnlyList<GraphNode> nodes)
    {
        Nodes = nodes;
    }

    /// <summary>Every node in the graph, in the order it was declared to the builder.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; }
}
