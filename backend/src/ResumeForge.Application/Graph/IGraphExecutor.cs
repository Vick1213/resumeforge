namespace ResumeForge.Application.Graph;

/// <summary>Runs a validated <see cref="Graph"/> with maximum concurrency and failure isolation.</summary>
public interface IGraphExecutor
{
    /// <summary>
    /// Executes every node of <paramref name="graph"/>, starting each the moment its
    /// dependencies resolve, honouring <paramref name="ct"/>.
    /// </summary>
    /// <exception cref="GraphExecutionException">A node declared <c>.Critical()</c> failed.</exception>
    Task<GraphRunResult> RunAsync(Graph graph, IServiceProvider services, CancellationToken ct);
}
