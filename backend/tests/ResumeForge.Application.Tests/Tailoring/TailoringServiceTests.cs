using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Tests for <see cref="TailoringService"/>: it must orchestrate by running the graph and
/// reassembling the result from its outputs, not by executing pipeline logic itself.
/// </summary>
public sealed class TailoringServiceTests
{
    private static readonly ResumeForge.Application.Graph.Graph EmptyGraph = new GraphBuilder().Build();

    [Fact]
    public async Task TailorAsync_assembles_the_result_from_graph_outputs()
    {
        var document = TestData.Document();
        var executed = new CommandExecutionResult { Document = document, Diff = [] };
        var validation = new CommandValidationResult { Accepted = [], Rejected = [] };
        var coverage = new CoverageReport { Score = 0.75, Requirements = [] };

        var trace = new List<GraphNodeTrace> { new() { Node = "render", Status = GraphNodeStatus.Succeeded, Duration = TimeSpan.FromMilliseconds(5) } };
        var usage = new TokenUsage { InputTokens = 100, OutputTokens = 50, ModelCalls = 1, CacheHits = 0 };

        var runResult = new GraphRunResult
        {
            Trace = trace,
            Succeeded = true,
            Outputs = new Dictionary<string, object?>
            {
                [TailoringGraphFactory.ExecuteCommands] = executed,
                [TailoringGraphFactory.ValidateCommands] = validation,
                [TailoringGraphFactory.VerifyCoverage] = coverage,
            },
            Usage = usage,
        };

        var factory = Substitute.For<ITailoringGraphFactory>();
        factory.Create(Arg.Any<TailoringRequest>()).Returns(EmptyGraph);

        var executor = Substitute.For<IGraphExecutor>();
        executor.RunAsync(EmptyGraph, Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>()).Returns(runResult);

        var service = new TailoringService(factory, executor, Substitute.For<IServiceProvider>());

        var result = await service.TailorAsync(new TailoringRequest { JobId = "job-1" }, CancellationToken.None);

        result.Document.ShouldBe(document);
        result.Coverage.ShouldBe(coverage);
        result.Commands.ShouldBe(validation);
        result.Usage.ShouldBe(usage);
        result.Trace.ShouldBe(trace);
    }

    [Fact]
    public async Task TailorAsync_throws_when_execute_commands_output_is_missing()
    {
        var runResult = new GraphRunResult
        {
            Trace = [],
            Succeeded = false,
            Outputs = new Dictionary<string, object?>(),
            Usage = TokenUsage.Empty,
        };

        var factory = Substitute.For<ITailoringGraphFactory>();
        factory.Create(Arg.Any<TailoringRequest>()).Returns(EmptyGraph);

        var executor = Substitute.For<IGraphExecutor>();
        executor.RunAsync(Arg.Any<ResumeForge.Application.Graph.Graph>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>()).Returns(runResult);

        var service = new TailoringService(factory, executor, Substitute.For<IServiceProvider>());

        await Should.ThrowAsync<InvalidOperationException>(
            () => service.TailorAsync(new TailoringRequest { JobId = "job-1" }, CancellationToken.None));
    }

    [Fact]
    public async Task TailorAsync_defaults_coverage_when_verify_coverage_output_is_missing()
    {
        var document = TestData.Document();
        var executed = new CommandExecutionResult { Document = document, Diff = [] };
        var validation = new CommandValidationResult { Accepted = [], Rejected = [] };

        var runResult = new GraphRunResult
        {
            Trace = [],
            Succeeded = true,
            Outputs = new Dictionary<string, object?>
            {
                [TailoringGraphFactory.ExecuteCommands] = executed,
                [TailoringGraphFactory.ValidateCommands] = validation,
            },
            Usage = TokenUsage.Empty,
        };

        var factory = Substitute.For<ITailoringGraphFactory>();
        factory.Create(Arg.Any<TailoringRequest>()).Returns(EmptyGraph);

        var executor = Substitute.For<IGraphExecutor>();
        executor.RunAsync(Arg.Any<ResumeForge.Application.Graph.Graph>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>()).Returns(runResult);

        var service = new TailoringService(factory, executor, Substitute.For<IServiceProvider>());

        var result = await service.TailorAsync(new TailoringRequest { JobId = "job-1" }, CancellationToken.None);

        result.Coverage.Score.ShouldBe(0.0);
        result.Coverage.Requirements.ShouldBeEmpty();
    }
}
