namespace ResumeForge.Application.Graph;

/// <summary>
/// A single unit of work in a <see cref="Graph"/>: a name, the names of the nodes it
/// depends on, and an asynchronous body that reads upstream results from a
/// <see cref="GraphContext"/> and produces its own result.
/// </summary>
public interface IGraphNode
{
    /// <summary>The node's unique name within its graph.</summary>
    string Name { get; }

    /// <summary>The names of the nodes that must resolve before this node runs.</summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>Runs the node's body, producing its result.</summary>
    Task<object?> ExecuteAsync(GraphContext context, CancellationToken ct);
}
