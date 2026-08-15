namespace ResumeForge.Application.Graph;

/// <summary>
/// A concrete <see cref="IGraphNode"/> built from a delegate, carrying the scheduling
/// metadata (<see cref="Predicate"/>, <see cref="Critical"/>, <see cref="Label"/>) that
/// <see cref="GraphBuilder"/> attaches beyond the bare <see cref="IGraphNode"/> surface.
/// Not sealed so <see cref="GraphNode{TResult}"/> can specialize it with a typed body.
/// </summary>
public class GraphNode : IGraphNode
{
    private readonly Func<GraphContext, CancellationToken, Task<object?>> _body;

    /// <summary>Creates a node from an untyped, object-returning body.</summary>
    public GraphNode(
        string name,
        IReadOnlyList<string> dependsOn,
        Func<GraphContext, CancellationToken, Task<object?>> body,
        Func<GraphContext, bool>? predicate = null,
        bool critical = false,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(dependsOn);
        ArgumentNullException.ThrowIfNull(body);

        Name = name;
        DependsOn = dependsOn;
        _body = body;
        Predicate = predicate;
        Critical = critical;
        Label = label;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; }

    /// <summary>When set, the node runs only if this returns true; otherwise it is skipped.</summary>
    public Func<GraphContext, bool>? Predicate { get; }

    /// <summary>When true, this node's failure aborts the whole run with <see cref="GraphExecutionException"/>.</summary>
    public bool Critical { get; }

    /// <summary>Optional human-readable label for display in a trace viewer.</summary>
    public string? Label { get; }

    /// <inheritdoc />
    public virtual Task<object?> ExecuteAsync(GraphContext context, CancellationToken ct) => _body(context, ct);
}

/// <summary>
/// A <see cref="GraphNode"/> whose body is declared with a strongly-typed result, so
/// callers building the graph get compile-time checking of what a node produces without
/// having to cast to <c>object?</c> by hand. Downstream reads via
/// <see cref="GraphContext.Get{T}"/> remain runtime-checked either way.
/// </summary>
public sealed class GraphNode<TResult> : GraphNode
{
    /// <summary>Creates a node from a typed body.</summary>
    public GraphNode(
        string name,
        IReadOnlyList<string> dependsOn,
        Func<GraphContext, CancellationToken, Task<TResult>> body,
        Func<GraphContext, bool>? predicate = null,
        bool critical = false,
        string? label = null)
        : base(name, dependsOn, Wrap(body), predicate, critical, label)
    {
    }

    private static Func<GraphContext, CancellationToken, Task<object?>> Wrap(
        Func<GraphContext, CancellationToken, Task<TResult>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return async (context, ct) => await body(context, ct).ConfigureAwait(false);
    }
}
