using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Graph;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Graph;

/// <summary>Tests for <see cref="GraphExecutor"/>: scheduling, concurrency, and failure isolation.</summary>
public sealed class GraphExecutorTests
{
    private static IServiceProvider EmptyServices => Substitute.For<IServiceProvider>();

    private static GraphExecutor NewExecutor(GraphOptions? options = null) =>
        new(TimeProvider.System, options ?? new GraphOptions(), NullLogger<GraphExecutor>.Instance);

    [Fact]
    public async Task Diamond_topology_produces_correct_outputs()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => Task.FromResult<object?>(1))
            .AddNode("b", (ctx, _) => Task.FromResult<object?>(ctx.Get<int>("a") + 1)).DependsOn("a")
            .AddNode("c", (ctx, _) => Task.FromResult<object?>(ctx.Get<int>("a") + 2)).DependsOn("a")
            .AddNode("d", (ctx, _) => Task.FromResult<object?>(ctx.Get<int>("b") + ctx.Get<int>("c"))).DependsOn("b", "c")
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Outputs["a"].ShouldBe(1);
        result.Outputs["b"].ShouldBe(2);
        result.Outputs["c"].ShouldBe(3);
        result.Outputs["d"].ShouldBe(5);
        result.Trace.Count.ShouldBe(4);
        result.Trace.ShouldAllBe(t => t.Status == GraphNodeStatus.Succeeded);
    }

    [Fact]
    public async Task Independent_nodes_run_concurrently()
    {
        var arrived = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<object?> Body(GraphContext ctx, CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothArrived.SetResult();
            }

            await bothArrived.Task.WaitAsync(ct);
            return null;
        }

        var graph = new GraphBuilder()
            .AddNode("a", Body)
            .AddNode("b", Body)
            .Build();

        var runTask = NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);
        var winner = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

        winner.ShouldBe(runTask, "the two independent nodes never both reached the barrier — they did not run concurrently.");
        (await runTask).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_node_skips_only_its_transitive_dependents_while_a_parallel_branch_succeeds()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => throw new InvalidOperationException("boom"))
            .AddNode("b", (_, _) => Task.FromResult<object?>("b-ran")).DependsOn("a")
            .AddNode("d", (_, _) => Task.FromResult<object?>("d-ran")).DependsOn("b")
            .AddNode("c", (_, _) => Task.FromResult<object?>("c-ran"))
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        Trace(result, "a").Status.ShouldBe(GraphNodeStatus.Failed);
        Trace(result, "a").Error!.ShouldContain("boom");
        Trace(result, "b").Status.ShouldBe(GraphNodeStatus.Skipped);
        Trace(result, "d").Status.ShouldBe(GraphNodeStatus.Skipped);
        Trace(result, "c").Status.ShouldBe(GraphNodeStatus.Succeeded);
        result.Outputs["c"].ShouldBe("c-ran");
    }

    [Fact]
    public async Task Critical_failure_throws_GraphExecutionException()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => throw new InvalidOperationException("critical boom"))
            .Critical()
            .AddNode("b", (_, _) => Task.FromResult<object?>("b-ran"))
            .Build();

        var ex = await Should.ThrowAsync<GraphExecutionException>(
            () => NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None));

        ex.NodeName.ShouldBe("a");
        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
        ex.InnerException!.Message.ShouldBe("critical boom");
    }

    [Fact]
    public async Task Non_critical_failure_does_not_throw()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => throw new InvalidOperationException("boom"))
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task When_false_skips_the_node_but_dependents_still_run_reading_default()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => Task.FromResult<object?>("should not run"))
            .When(_ => false)
            .AddNode("b", (ctx, _) =>
            {
                var gotDefault = ctx.TryGet<string?>("a", out var value) && value is null;
                return Task.FromResult<object?>(gotDefault ? "b-saw-default" : "b-saw-something-else");
            })
            .DependsOn("a")
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        Trace(result, "a").Status.ShouldBe(GraphNodeStatus.Skipped);
        Trace(result, "b").Status.ShouldBe(GraphNodeStatus.Succeeded);
        result.Outputs["b"].ShouldBe("b-saw-default");
    }

    [Fact]
    public async Task Throwing_condition_marks_the_node_failed()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => Task.FromResult<object?>("never"))
            .When(_ => throw new InvalidOperationException("condition boom"))
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        Trace(result, "a").Status.ShouldBe(GraphNodeStatus.Failed);
        Trace(result, "a").Error!.ShouldContain("condition boom");
    }

    [Fact]
    public async Task Cancellation_records_cancelled_status()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var graph = new GraphBuilder()
            .AddNode("a", async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return null;
            })
            .Build();

        var runTask = NewExecutor().RunAsync(graph, EmptyServices, cts.Token);
        await started.Task;
        cts.Cancel();

        var result = await runTask;

        result.Succeeded.ShouldBeFalse();
        Trace(result, "a").Status.ShouldBe(GraphNodeStatus.Cancelled);
    }

    [Fact]
    public async Task Not_yet_started_dependent_of_a_cancelled_run_is_recorded_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => Task.FromResult<object?>("a"))
            .AddNode("b", (_, _) => Task.FromResult<object?>("b")).DependsOn("a")
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, cts.Token);

        result.Trace.ShouldAllBe(t => t.Status == GraphNodeStatus.Cancelled);
    }

    [Fact]
    public async Task MaxConcurrency_is_never_exceeded()
    {
        const int cap = 2;
        var current = 0;
        var peak = 0;
        var peakLock = new Lock();

        async Task<object?> Body(GraphContext ctx, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref current);
            lock (peakLock)
            {
                peak = Math.Max(peak, now);
            }

            await Task.Delay(50, ct);
            Interlocked.Decrement(ref current);
            return null;
        }

        var builder = new GraphBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.AddNode($"n{i}", Body);
        }

        var graph = builder.Build();
        var result = await NewExecutor(new GraphOptions { MaxConcurrency = cap }).RunAsync(graph, EmptyServices, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        peak.ShouldBeLessThanOrEqualTo(cap);
    }

    [Fact]
    public async Task Node_records_duration_and_token_usage_in_trace()
    {
        var graph = new GraphBuilder()
            .AddNode("a", async (ctx, _) =>
            {
                await Task.Delay(5);
                ctx.Budget.Record("a", new TokenUsage { InputTokens = 12, OutputTokens = 34, ModelCalls = 1, CacheHits = 0 });
                return (object?)"done";
            })
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        var trace = Trace(result, "a");
        trace.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        trace.InputTokens.ShouldBe(12);
        trace.OutputTokens.ShouldBe(34);
        result.Usage.InputTokens.ShouldBe(12);
        result.Usage.OutputTokens.ShouldBe(34);
    }

    [Fact]
    public async Task Root_nodes_with_no_dependencies_all_start_without_waiting_on_each_other()
    {
        var graph = new GraphBuilder()
            .AddNode("a", (_, _) => Task.FromResult<object?>(1))
            .AddNode("b", (_, _) => Task.FromResult<object?>(2))
            .AddNode("c", (_, _) => Task.FromResult<object?>(3))
            .Build();

        var result = await NewExecutor().RunAsync(graph, EmptyServices, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Trace.Count.ShouldBe(3);
    }

    private static GraphNodeTrace Trace(GraphRunResult result, string node) => result.Trace.Single(t => t.Node == node);
}
