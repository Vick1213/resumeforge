using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>Tests for the <see cref="CachingLanguageModel"/> decorator.</summary>
public sealed class CachingLanguageModelTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose() => _fixture.Dispose();

    private sealed record Dummy(string Value);

    private static ILanguageModel NewInner(string modelId, Func<Dummy> valueFactory)
    {
        var inner = Substitute.For<ILanguageModel>();
        inner.ModelId.Returns(modelId);
        inner.CompleteAsync<Dummy>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ModelResponse<Dummy>
            {
                Value = valueFactory(),
                Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5, ModelCalls = 1, CacheHits = 0 },
                FromCache = false,
            }));

        return inner;
    }

    [Fact]
    public async Task Bypasses_the_cache_entirely_when_CacheKey_is_null()
    {
        var inner = NewInner("m1", () => new Dummy("x"));
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);
        var request = new ModelRequest { System = "s", User = "u", SchemaName = "schema-a", CacheKey = null };

        var response = await caching.CompleteAsync<Dummy>(request, CancellationToken.None);

        response.FromCache.ShouldBeFalse();
        (await _fixture.Context.ModelCacheEntries.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        await inner.Received(1).CompleteAsync<Dummy>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task First_call_is_a_miss_and_the_second_identical_call_is_a_cache_hit()
    {
        var callCount = 0;
        var inner = NewInner("m1", () => { callCount++; return new Dummy("cached-value"); });
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);
        var request = new ModelRequest { System = "s", User = "u", SchemaName = "schema-a", CacheKey = "key-1" };

        var first = await caching.CompleteAsync<Dummy>(request, CancellationToken.None);
        first.FromCache.ShouldBeFalse();
        first.Value.Value.ShouldBe("cached-value");

        var second = await caching.CompleteAsync<Dummy>(request, CancellationToken.None);
        second.FromCache.ShouldBeTrue();
        second.Value.Value.ShouldBe("cached-value");
        second.Usage.CacheHits.ShouldBe(1);
        second.Usage.ModelCalls.ShouldBe(0);

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_different_schema_name_produces_a_different_cache_key()
    {
        var inner = NewInner("m1", () => new Dummy("v"));
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);

        await caching.CompleteAsync<Dummy>(new ModelRequest { System = "s", User = "u", SchemaName = "schema-a", CacheKey = "same-key" }, CancellationToken.None);
        await caching.CompleteAsync<Dummy>(new ModelRequest { System = "s", User = "u", SchemaName = "schema-b", CacheKey = "same-key" }, CancellationToken.None);

        (await _fixture.Context.ModelCacheEntries.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        await inner.Received(2).CompleteAsync<Dummy>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_different_user_prompt_produces_a_different_cache_key()
    {
        var inner = NewInner("m1", () => new Dummy("v"));
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);

        await caching.CompleteAsync<Dummy>(new ModelRequest { System = "s", User = "u1", SchemaName = "schema-a", CacheKey = "same-key" }, CancellationToken.None);
        await caching.CompleteAsync<Dummy>(new ModelRequest { System = "s", User = "u2", SchemaName = "schema-a", CacheKey = "same-key" }, CancellationToken.None);

        (await _fixture.Context.ModelCacheEntries.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task ModelId_delegates_to_the_inner_model()
    {
        var inner = NewInner("anthropic-model-x", () => new Dummy("v"));
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);

        caching.ModelId.ShouldBe("anthropic-model-x");
    }

    [Fact]
    public async Task Persists_the_input_and_output_token_counts_from_the_original_response()
    {
        var inner = NewInner("m1", () => new Dummy("v"));
        var caching = new CachingLanguageModel(inner, _fixture.Context, _time);
        var request = new ModelRequest { System = "s", User = "u", SchemaName = "schema-a", CacheKey = "key-1" };

        await caching.CompleteAsync<Dummy>(request, CancellationToken.None);

        var entry = await _fixture.Context.ModelCacheEntries.SingleAsync(TestContext.Current.CancellationToken);
        entry.InputTokens.ShouldBe(10);
        entry.OutputTokens.ShouldBe(5);
        entry.ModelId.ShouldBe("m1");
        entry.SchemaName.ShouldBe("schema-a");
    }
}
