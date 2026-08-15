using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResumeForge.Application.Graph;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Graph;

/// <summary>Tests for <see cref="GraphContext"/>.</summary>
public sealed class GraphContextTests
{
    private static GraphContext NewContext() => new(Substitute.For<IServiceProvider>(), new TokenBudget());

    [Fact]
    public void Get_throws_clear_exception_naming_node_when_missing()
    {
        var ctx = NewContext();

        var ex = Should.Throw<KeyNotFoundException>(() => ctx.Get<string>("missing-node"));
        ex.Message.ShouldContain("missing-node");
    }

    [Fact]
    public void Get_throws_when_stored_value_has_wrong_type()
    {
        var ctx = NewContext();
        ctx.Set("node", 42);

        var ex = Should.Throw<InvalidOperationException>(() => ctx.Get<string>("node"));
        ex.Message.ShouldContain("node");
    }

    [Fact]
    public void Get_returns_stored_value_of_matching_type()
    {
        var ctx = NewContext();
        ctx.Set("node", "hello");

        ctx.Get<string>("node").ShouldBe("hello");
    }

    [Fact]
    public void TryGet_returns_false_without_throwing_when_missing()
    {
        var ctx = NewContext();

        ctx.TryGet<string>("missing", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void TryGet_returns_true_when_present_and_matching()
    {
        var ctx = NewContext();
        ctx.Set("node", 7);

        ctx.TryGet<int>("node", out var value).ShouldBeTrue();
        value.ShouldBe(7);
    }

    [Fact]
    public void Skipped_node_yields_default_via_Get()
    {
        var ctx = NewContext();
        ctx.SetSkipped("node");

        ctx.Get<int>("node").ShouldBe(0);
        ctx.Get<string?>("node").ShouldBeNull();
    }

    [Fact]
    public void Skipped_node_yields_default_via_TryGet()
    {
        var ctx = NewContext();
        ctx.SetSkipped("node");

        ctx.TryGet<int>("node", out var value).ShouldBeTrue();
        value.ShouldBe(0);
    }

    [Fact]
    public void Trace_is_empty_until_nodes_are_added()
    {
        var ctx = NewContext();
        ctx.Trace.ShouldBeEmpty();

        ctx.AddTrace(new GraphNodeTrace { Node = "a", Status = GraphNodeStatus.Succeeded, Duration = TimeSpan.Zero });
        ctx.Trace.Count.ShouldBe(1);
    }
}
