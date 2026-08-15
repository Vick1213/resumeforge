namespace ResumeForge.Application.Graph;

/// <summary>Thrown by <see cref="GraphBuilder.Build"/> when a node's declared dependency does not exist.</summary>
public sealed class GraphValidationException : Exception
{
    /// <summary>Creates a new validation exception with the given message.</summary>
    public GraphValidationException(string message) : base(message)
    {
    }

    /// <summary>Creates a new validation exception with the given message and inner exception.</summary>
    public GraphValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown by <see cref="GraphBuilder.Build"/> when the declared dependency edges contain a
/// cycle. <see cref="Exception.Message"/> names the full cycle path, e.g. <c>"a -> b -> c -> a"</c>.
/// </summary>
public sealed class GraphCycleException : Exception
{
    /// <summary>Creates a new cycle exception with the given message.</summary>
    public GraphCycleException(string message) : base(message)
    {
    }

    /// <summary>Creates a new cycle exception with the given message and inner exception.</summary>
    public GraphCycleException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown by <see cref="GraphExecutor.RunAsync"/> when a node declared <c>.Critical()</c>
/// fails. Wraps the original exception thrown by the node's body.
/// </summary>
public sealed class GraphExecutionException : Exception
{
    /// <summary>Creates a new execution exception naming the failed critical node.</summary>
    public GraphExecutionException(string nodeName, Exception innerException)
        : base($"Critical graph node '{nodeName}' failed: {innerException.Message}", innerException)
    {
        NodeName = nodeName;
    }

    /// <summary>The name of the critical node that failed.</summary>
    public string NodeName { get; }
}

/// <summary>Thrown by <see cref="TokenBudget.Record"/> when recording usage would exceed a configured ceiling.</summary>
public sealed class TokenBudgetExceededException : Exception
{
    /// <summary>Creates a new token-budget-exceeded exception with the given message.</summary>
    public TokenBudgetExceededException(string message) : base(message)
    {
    }

    /// <summary>Creates a new token-budget-exceeded exception with the given message and inner exception.</summary>
    public TokenBudgetExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
