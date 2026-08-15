using ResumeForge.Application.Graph;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Graph;

/// <summary>Tests for <see cref="GraphBuilder"/> validation.</summary>
public sealed class GraphBuilderTests
{
    private static Func<GraphContext, CancellationToken, Task<object?>> NoOp() => (_, _) => Task.FromResult<object?>(null);

    [Fact]
    public void Build_throws_for_unknown_dependency()
    {
        var builder = new GraphBuilder();
        builder.AddNode("a", NoOp()).DependsOn("ghost");

        var ex = Should.Throw<GraphValidationException>(() => builder.Build());
        ex.Message.ShouldContain("ghost");
        ex.Message.ShouldContain("a");
    }

    [Fact]
    public void Build_throws_for_duplicate_node_name()
    {
        var builder = new GraphBuilder();
        builder.AddNode("a", NoOp());

        Should.Throw<GraphValidationException>(() => builder.AddNode("a", NoOp()));
    }

    [Fact]
    public void Build_detects_a_direct_cycle_and_names_the_path()
    {
        var builder = new GraphBuilder();
        builder.AddNode("a", NoOp()).DependsOn("b");
        builder.AddNode("b", NoOp()).DependsOn("a");

        var ex = Should.Throw<GraphCycleException>(() => builder.Build());
        ex.Message.ShouldContain("a -> b -> a");
    }

    [Fact]
    public void Build_detects_a_three_node_cycle_and_names_the_full_path()
    {
        var builder = new GraphBuilder();
        builder.AddNode("a", NoOp()).DependsOn("c");
        builder.AddNode("b", NoOp()).DependsOn("a");
        builder.AddNode("c", NoOp()).DependsOn("b");

        var ex = Should.Throw<GraphCycleException>(() => builder.Build());
        ex.Message.ShouldContain("->");
        // The cycle path always contains exactly the three participating nodes plus the repeated start.
        var arrowCount = ex.Message.Split("->").Length - 1;
        arrowCount.ShouldBe(3);
    }

    [Fact]
    public void Build_succeeds_for_a_valid_diamond()
    {
        var builder = new GraphBuilder();
        builder.AddNode("a", NoOp());
        builder.AddNode("b", NoOp()).DependsOn("a");
        builder.AddNode("c", NoOp()).DependsOn("a");
        builder.AddNode("d", NoOp()).DependsOn("b", "c");

        var graph = builder.Build();

        graph.Nodes.Count.ShouldBe(4);
        graph.Nodes.Select(n => n.Name).ShouldBe(["a", "b", "c", "d"]);
    }

    [Fact]
    public void Fluent_chain_through_NodeConfigurator_builds_the_same_graph()
    {
        var graph = new GraphBuilder()
            .AddNode("a", NoOp())
            .AddNode("b", NoOp()).DependsOn("a")
            .Critical()
            .WithLabel("Step B")
            .Build();

        graph.Nodes.Count.ShouldBe(2);
        var b = graph.Nodes.Single(n => n.Name == "b");
        b.Critical.ShouldBeTrue();
        b.Label.ShouldBe("Step B");
        b.DependsOn.ShouldBe(["a"]);
    }
}
