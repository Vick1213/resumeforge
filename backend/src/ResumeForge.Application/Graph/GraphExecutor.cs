using Microsoft.Extensions.Logging;
using ResumeForge.Application.Abstractions;

namespace ResumeForge.Application.Graph;

/// <summary>
/// Executes a <see cref="Graph"/> with maximum concurrency: every node starts the moment
/// its dependencies resolve, gated only by a <see cref="SemaphoreSlim"/> capped at
/// <see cref="GraphOptions.MaxConcurrency"/>. Failures isolate to their own branch —
/// a failed node's transitive dependents are recorded <see cref="GraphNodeStatus.Skipped"/>
/// while independent branches run to completion — unless the failed node was declared
/// <c>.Critical()</c>, in which case the whole run throws <see cref="GraphExecutionException"/>.
/// </summary>
public sealed class GraphExecutor(TimeProvider timeProvider, GraphOptions options, ILogger<GraphExecutor> logger)
    : IGraphExecutor
{
    /// <summary>Creates an executor with default options.</summary>
    public GraphExecutor(TimeProvider timeProvider, ILogger<GraphExecutor> logger)
        : this(timeProvider, new GraphOptions(), logger)
    {
    }

    /// <inheritdoc />
    public async Task<GraphRunResult> RunAsync(Graph graph, IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(services);

        var maxConcurrency = Math.Max(1, options.MaxConcurrency);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var budget = new TokenBudget();
        var context = new GraphContext(services, budget);
        var failures = new Dictionary<string, Exception>(StringComparer.Ordinal);
        var failuresLock = new Lock();

        var completions = graph.Nodes.ToDictionary(
            n => n.Name,
            _ => new TaskCompletionSource<NodeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            _ = DriveNodeAsync(node, completions, context, semaphore, budget, failures, failuresLock, ct);
        }

        await Task.WhenAll(completions.Values.Select(tcs => tcs.Task)).ConfigureAwait(false);

        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        var anyUnsuccessful = false;
        Exception? criticalFailure = null;
        string? criticalFailureNode = null;

        foreach (var node in graph.Nodes)
        {
            outputs[node.Name] = context.TryGetRawForOutput(node.Name, out var value) ? value : null;

            var outcome = completions[node.Name].Task.Result;

            if (outcome is NodeOutcome.Failed or NodeOutcome.Cancelled)
            {
                anyUnsuccessful = true;
            }

            if (outcome == NodeOutcome.Failed && node.Critical && criticalFailure is null)
            {
                lock (failuresLock)
                {
                    if (failures.TryGetValue(node.Name, out var ex))
                    {
                        criticalFailure = ex;
                        criticalFailureNode = node.Name;
                    }
                }
            }
        }

        if (criticalFailure is not null && criticalFailureNode is not null)
        {
            throw new GraphExecutionException(criticalFailureNode, criticalFailure);
        }

        return new GraphRunResult
        {
            Trace = context.Trace,
            Succeeded = !anyUnsuccessful,
            Outputs = outputs,
            Usage = budget.Total,
        };
    }

    private async Task DriveNodeAsync(
        GraphNode node,
        Dictionary<string, TaskCompletionSource<NodeOutcome>> completions,
        GraphContext context,
        SemaphoreSlim semaphore,
        TokenBudget budget,
        Dictionary<string, Exception> failures,
        Lock failuresLock,
        CancellationToken ct)
    {
        var tcs = completions[node.Name];

        try
        {
            var depOutcomes = node.DependsOn.Count == 0
                ? Array.Empty<NodeOutcome>()
                : await Task.WhenAll(node.DependsOn.Select(d => completions[d].Task)).ConfigureAwait(false);

            var cancelledDependency = false;
            string? cascadeReason = null;

            for (var i = 0; i < node.DependsOn.Count; i++)
            {
                var depOutcome = depOutcomes[i];

                if (depOutcome == NodeOutcome.Cancelled)
                {
                    cancelledDependency = true;
                    break;
                }

                if (cascadeReason is null && depOutcome is NodeOutcome.Failed or NodeOutcome.SkippedCascade)
                {
                    cascadeReason = $"upstream dependency '{node.DependsOn[i]}' did not succeed ({depOutcome}).";
                }
            }

            if (ct.IsCancellationRequested || cancelledDependency)
            {
                context.SetSkipped(node.Name);
                RecordTrace(context, node.Name, GraphNodeStatus.Cancelled, TimeSpan.Zero, "The run was cancelled.", 0, 0);
                tcs.SetResult(NodeOutcome.Cancelled);
                return;
            }

            if (cascadeReason is not null)
            {
                context.SetSkipped(node.Name);
                RecordTrace(context, node.Name, GraphNodeStatus.Skipped, TimeSpan.Zero, cascadeReason, 0, 0);
                tcs.SetResult(NodeOutcome.SkippedCascade);
                return;
            }

            if (node.Predicate is { } predicate)
            {
                bool shouldRun;
                try
                {
                    shouldRun = predicate(context);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Graph node '{Node}' condition threw; treating as failed.", node.Name);
                    RecordFailure(node.Name, ex, failures, failuresLock);
                    RecordTrace(context, node.Name, GraphNodeStatus.Failed, TimeSpan.Zero, $"Condition threw: {ex.Message}", 0, 0);
                    tcs.SetResult(NodeOutcome.Failed);
                    return;
                }

                if (!shouldRun)
                {
                    context.SetSkipped(node.Name);
                    RecordTrace(context, node.Name, GraphNodeStatus.Skipped, TimeSpan.Zero, "Condition evaluated to false.", 0, 0);
                    tcs.SetResult(NodeOutcome.SkippedCondition);
                    return;
                }
            }

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            var start = timeProvider.GetTimestamp();

            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await node.ExecuteAsync(context, ct).ConfigureAwait(false);
                var duration = timeProvider.GetElapsedTime(start);
                context.Set(node.Name, result);
                var usage = budget.ForNode(node.Name);
                RecordTrace(context, node.Name, GraphNodeStatus.Succeeded, duration, null, usage.InputTokens, usage.OutputTokens);
                tcs.SetResult(NodeOutcome.Succeeded);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var duration = timeProvider.GetElapsedTime(start);
                context.SetSkipped(node.Name);
                RecordTrace(context, node.Name, GraphNodeStatus.Cancelled, duration, "The run was cancelled.", 0, 0);
                tcs.SetResult(NodeOutcome.Cancelled);
            }
            catch (Exception ex)
            {
                var duration = timeProvider.GetElapsedTime(start);
                logger.LogWarning(ex, "Graph node '{Node}' failed.", node.Name);
                RecordFailure(node.Name, ex, failures, failuresLock);
                context.SetSkipped(node.Name);
                var usage = budget.ForNode(node.Name);
                RecordTrace(context, node.Name, GraphNodeStatus.Failed, duration, ex.Message, usage.InputTokens, usage.OutputTokens);
                tcs.SetResult(NodeOutcome.Failed);
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            // Defensive: dependency tasks never fault (they always SetResult), so this
            // path only guards against a truly unexpected error in the scheduling logic
            // itself, ensuring the run's overall Task.WhenAll can never hang.
            logger.LogError(ex, "Unexpected scheduling error for graph node '{Node}'.", node.Name);
            RecordFailure(node.Name, ex, failures, failuresLock);
            tcs.TrySetResult(NodeOutcome.Failed);
        }
    }

    private static void RecordFailure(string nodeName, Exception ex, Dictionary<string, Exception> failures, Lock failuresLock)
    {
        lock (failuresLock)
        {
            failures[nodeName] = ex;
        }
    }

    private static void RecordTrace(
        GraphContext context,
        string node,
        GraphNodeStatus status,
        TimeSpan duration,
        string? error,
        int inputTokens,
        int outputTokens)
    {
        context.AddTrace(new GraphNodeTrace
        {
            Node = node,
            Status = status,
            Duration = duration,
            Error = error,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
        });
    }

    private enum NodeOutcome
    {
        Succeeded,
        Failed,
        SkippedCondition,
        SkippedCascade,
        Cancelled,
    }
}
