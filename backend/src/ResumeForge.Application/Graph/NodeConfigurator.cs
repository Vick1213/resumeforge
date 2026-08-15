namespace ResumeForge.Application.Graph;

/// <summary>
/// Fluent configuration surface for a node just added to a <see cref="GraphBuilder"/>.
/// Every method returns itself so calls chain, and also exposes <see cref="AddNode"/>/
/// <see cref="Build"/> pass-throughs so a whole graph can be declared as one expression.
/// </summary>
public sealed class NodeConfigurator
{
    private readonly GraphBuilder _builder;
    private readonly GraphBuilder.PendingNode _node;

    internal NodeConfigurator(GraphBuilder builder, GraphBuilder.PendingNode node)
    {
        _builder = builder;
        _node = node;
    }

    /// <summary>Declares that this node depends on the named upstream nodes.</summary>
    public NodeConfigurator DependsOn(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _node.DependsOn.AddRange(names);
        return this;
    }

    /// <summary>
    /// Declares a condition gating this node. When <paramref name="predicate"/> evaluates
    /// false at run time, the node is recorded <see cref="GraphNodeStatus.Skipped"/> and
    /// its dependents still run, reading <c>default</c> for its result.
    /// </summary>
    public NodeConfigurator When(Func<GraphContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _node.Predicate = predicate;
        return this;
    }

    /// <summary>
    /// Marks this node critical: if it fails, <see cref="GraphExecutor.RunAsync"/> throws
    /// <see cref="GraphExecutionException"/> instead of returning a partial result.
    /// </summary>
    public NodeConfigurator Critical()
    {
        _node.Critical = true;
        return this;
    }

    /// <summary>Attaches a human-readable label, shown by a trace viewer in place of the raw node name.</summary>
    public NodeConfigurator WithLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _node.Label = label;
        return this;
    }

    /// <summary>Pass-through to <see cref="GraphBuilder.AddNode(string, Func{GraphContext, CancellationToken, Task{object}})"/>, to keep a whole graph declaration chained.</summary>
    public NodeConfigurator AddNode(string name, Func<GraphContext, CancellationToken, Task<object?>> body) =>
        _builder.AddNode(name, body);

    /// <summary>Pass-through to the strongly-typed overload of <see cref="GraphBuilder.AddNode{TResult}"/>.</summary>
    public NodeConfigurator AddNode<TResult>(string name, Func<GraphContext, CancellationToken, Task<TResult>> body) =>
        _builder.AddNode(name, body);

    /// <summary>Pass-through to <see cref="GraphBuilder.Build"/>, to finish a chained declaration.</summary>
    public Graph Build() => _builder.Build();
}
