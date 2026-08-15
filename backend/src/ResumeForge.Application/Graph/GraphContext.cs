namespace ResumeForge.Application.Graph;

/// <summary>
/// Shared, thread-safe state for one graph run: the results nodes have produced so far,
/// the accumulating trace, and access to the ambient <see cref="IServiceProvider"/> and
/// <see cref="ITokenBudget"/>. Every node in a run shares the same instance, and multiple
/// nodes may read and write it concurrently.
/// </summary>
public sealed class GraphContext
{
    private static readonly object SkippedSentinel = new();

    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Lock _valuesLock = new();
    private readonly List<GraphNodeTrace> _trace = [];
    private readonly Lock _traceLock = new();

    /// <summary>Creates a context for one run, scoped to <paramref name="services"/> and <paramref name="budget"/>.</summary>
    public GraphContext(IServiceProvider services, ITokenBudget budget)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(budget);

        Services = services;
        Budget = budget;
    }

    /// <summary>The service provider nodes may use to resolve their own dependencies.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The token budget nodes record their model spend against.</summary>
    public ITokenBudget Budget { get; }

    /// <summary>The trace recorded so far, in the order nodes finished.</summary>
    public IReadOnlyList<GraphNodeTrace> Trace
    {
        get
        {
            lock (_traceLock)
            {
                return [.. _trace];
            }
        }
    }

    /// <summary>
    /// Reads the result of upstream node <paramref name="nodeName"/> as <typeparamref name="T"/>.
    /// A node skipped by a false <c>.When(...)</c> condition yields <c>default(T)</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No node named <paramref name="nodeName"/> has produced a result.</exception>
    /// <exception cref="InvalidOperationException">The stored result is not assignable to <typeparamref name="T"/>.</exception>
    public T Get<T>(string nodeName)
    {
        object? raw;
        bool present;

        lock (_valuesLock)
        {
            present = _values.TryGetValue(nodeName, out raw);
        }

        if (!present)
        {
            throw new KeyNotFoundException(
                $"Graph node '{nodeName}' has not produced a result. It may not exist, may not have run yet, " +
                "or may not be a declared dependency of the node reading it.");
        }

        if (ReferenceEquals(raw, SkippedSentinel))
        {
            return default!;
        }

        if (raw is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"Graph node '{nodeName}' produced a null result, but '{typeof(T).Name}' does not accept null.");
        }

        if (raw is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Graph node '{nodeName}' produced a result of type '{raw.GetType().Name}', but '{typeof(T).Name}' was requested.");
    }

    /// <summary>Attempts to read the result of upstream node <paramref name="nodeName"/> without throwing.</summary>
    public bool TryGet<T>(string nodeName, out T value)
    {
        lock (_valuesLock)
        {
            if (_values.TryGetValue(nodeName, out var raw))
            {
                if (ReferenceEquals(raw, SkippedSentinel) || (raw is null && default(T) is null))
                {
                    value = default!;
                    return true;
                }

                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, making it readable
    /// via <see cref="Get{T}"/>/<see cref="TryGet{T}"/>. The executor calls this to record
    /// each node's own result under its node name; node bodies may also call it directly
    /// to publish side-channel values under other keys.
    /// </summary>
    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_valuesLock)
        {
            _values[key] = value;
        }
    }

    /// <summary>Marks <paramref name="nodeName"/> as skipped, so reads of it yield <c>default</c> rather than throwing.</summary>
    internal void SetSkipped(string nodeName)
    {
        lock (_valuesLock)
        {
            _values[nodeName] = SkippedSentinel;
        }
    }

    /// <summary>Reads the raw stored value for <paramref name="name"/>, translating the skip sentinel to null.</summary>
    internal bool TryGetRawForOutput(string name, out object? value)
    {
        lock (_valuesLock)
        {
            if (_values.TryGetValue(name, out var raw))
            {
                value = ReferenceEquals(raw, SkippedSentinel) ? null : raw;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Appends <paramref name="trace"/> to the run's trace.</summary>
    internal void AddTrace(GraphNodeTrace trace)
    {
        lock (_traceLock)
        {
            _trace.Add(trace);
        }
    }
}
