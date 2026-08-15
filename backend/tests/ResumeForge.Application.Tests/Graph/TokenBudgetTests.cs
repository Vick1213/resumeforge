using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Graph;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Graph;

/// <summary>Tests for <see cref="TokenBudget"/>.</summary>
public sealed class TokenBudgetTests
{
    [Fact]
    public void Record_accumulates_total()
    {
        var budget = new TokenBudget();

        budget.Record("a", new TokenUsage { InputTokens = 10, OutputTokens = 5, ModelCalls = 1, CacheHits = 0 });
        budget.Record("b", new TokenUsage { InputTokens = 3, OutputTokens = 2, ModelCalls = 1, CacheHits = 1 });

        budget.Total.InputTokens.ShouldBe(13);
        budget.Total.OutputTokens.ShouldBe(7);
        budget.Total.ModelCalls.ShouldBe(2);
        budget.Total.CacheHits.ShouldBe(1);
    }

    [Fact]
    public void ForNode_accumulates_per_node_and_is_independent()
    {
        var budget = new TokenBudget();

        budget.Record("a", new TokenUsage { InputTokens = 10, OutputTokens = 0, ModelCalls = 1, CacheHits = 0 });
        budget.Record("a", new TokenUsage { InputTokens = 5, OutputTokens = 0, ModelCalls = 1, CacheHits = 0 });
        budget.Record("b", new TokenUsage { InputTokens = 1, OutputTokens = 0, ModelCalls = 1, CacheHits = 0 });

        budget.ForNode("a").InputTokens.ShouldBe(15);
        budget.ForNode("b").InputTokens.ShouldBe(1);
    }

    [Fact]
    public void ForNode_returns_empty_for_unknown_node()
    {
        var budget = new TokenBudget();
        budget.ForNode("nope").ShouldBe(TokenUsage.Empty);
    }

    [Fact]
    public void Ceiling_throws_when_exceeded()
    {
        var budget = new TokenBudget(ceiling: 10);

        Should.Throw<TokenBudgetExceededException>(() =>
            budget.Record("a", new TokenUsage { InputTokens = 8, OutputTokens = 8, ModelCalls = 1, CacheHits = 0 }));
    }

    [Fact]
    public void Ceiling_allows_recording_up_to_the_limit()
    {
        var budget = new TokenBudget(ceiling: 10);

        Should.NotThrow(() => budget.Record("a", new TokenUsage { InputTokens = 5, OutputTokens = 5, ModelCalls = 1, CacheHits = 0 }));
        budget.Total.InputTokens.ShouldBe(5);
    }

    [Fact]
    public void Record_is_thread_safe_under_concurrent_calls()
    {
        var budget = new TokenBudget();
        var usage = new TokenUsage { InputTokens = 1, OutputTokens = 1, ModelCalls = 1, CacheHits = 0 };

        Parallel.For(0, 1000, i => budget.Record($"node-{i % 10}", usage));

        budget.Total.InputTokens.ShouldBe(1000);
        budget.Total.ModelCalls.ShouldBe(1000);
    }
}
