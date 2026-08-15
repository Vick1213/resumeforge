namespace ResumeForge.Application.Graph;

/// <summary>
/// Fluent builder for declaring a <see cref="Graph"/> as data rather than as a
/// straight-line method. Call <see cref="AddNode"/> for each unit of work, chain
/// <see cref="NodeConfigurator"/> calls to declare its dependencies and behavior, and
/// finish with <see cref="Build"/>, which validates the whole graph before returning it.
/// </summary>
public sealed class GraphBuilder
{
    private readonly List<PendingNode> _pending = [];
    private readonly Dictionary<string, PendingNode> _byName = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a new node named <paramref name="name"/> whose body is <paramref name="body"/>.
    /// Returns a <see cref="NodeConfigurator"/> for declaring its dependencies and behavior.
    /// </summary>
    /// <exception cref="GraphValidationException">A node named <paramref name="name"/> already exists.</exception>
    public NodeConfigurator AddNode(string name, Func<GraphContext, CancellationToken, Task<object?>> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);

        if (!_byName.ContainsKey(name))
        {
            var pending = new PendingNode(name, body);
            _pending.Add(pending);
            _byName.Add(name, pending);
            return new NodeConfigurator(this, pending);
        }

        throw new GraphValidationException($"A node named '{name}' has already been added to this graph.");
    }

    /// <summary>
    /// Declares a new node named <paramref name="name"/> whose strongly-typed body is
    /// <paramref name="body"/>. Equivalent to <see cref="AddNode(string, Func{GraphContext, CancellationToken, Task{object}})"/>
    /// with the result boxed automatically.
    /// </summary>
    public NodeConfigurator AddNode<TResult>(string name, Func<GraphContext, CancellationToken, Task<TResult>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return AddNode(name, async (ctx, ct) => (object?)await body(ctx, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Validates every declared dependency name exists and that the edges contain no
    /// cycle, then returns the finished, immutable <see cref="Graph"/>.
    /// </summary>
    /// <exception cref="GraphValidationException">A node depends on a name that was never added.</exception>
    /// <exception cref="GraphCycleException">The dependency edges contain a cycle.</exception>
    public Graph Build()
    {
        ValidateDependenciesExist();
        DetectCycles();

        var nodes = _pending
            .Select(p => (GraphNode)new GraphNode(
                p.Name,
                [.. p.DependsOn],
                p.Body,
                p.Predicate,
                p.Critical,
                p.Label))
            .ToList();

        return new Graph(nodes);
    }

    private void ValidateDependenciesExist()
    {
        foreach (var node in _pending)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!_byName.ContainsKey(dep))
                {
                    throw new GraphValidationException(
                        $"Node '{node.Name}' depends on unknown node '{dep}'.");
                }
            }
        }
    }

    private void DetectCycles()
    {
        var state = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var node in _pending)
        {
            if (!state.TryGetValue(node.Name, out var s) || s == VisitState.Unvisited)
            {
                Visit(node.Name);
            }
        }

        void Visit(string name)
        {
            state[name] = VisitState.InProgress;
            path.Add(name);

            foreach (var dep in _byName[name].DependsOn)
            {
                if (state.TryGetValue(dep, out var depState))
                {
                    if (depState == VisitState.InProgress)
                    {
                        var cycleStart = path.IndexOf(dep);
                        var cycle = path.GetRange(cycleStart, path.Count - cycleStart);
                        cycle.Add(dep);
                        throw new GraphCycleException($"Cycle detected in graph: {string.Join(" -> ", cycle)}");
                    }

                    if (depState == VisitState.Done)
                    {
                        continue;
                    }
                }

                Visit(dep);
            }

            path.RemoveAt(path.Count - 1);
            state[name] = VisitState.Done;
        }
    }

    private enum VisitState
    {
        Unvisited,
        InProgress,
        Done,
    }

    /// <summary>Mutable, in-progress node declaration, finalized by <see cref="Build"/>.</summary>
    internal sealed class PendingNode(string name, Func<GraphContext, CancellationToken, Task<object?>> body)
    {
        public string Name { get; } = name;

        public Func<GraphContext, CancellationToken, Task<object?>> Body { get; } = body;

        public List<string> DependsOn { get; } = [];

        public Func<GraphContext, bool>? Predicate { get; set; }

        public bool Critical { get; set; }

        public string? Label { get; set; }
    }
}
