using ResumeForge.Application.Autofill;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>Tests for <see cref="LearnedFieldMapRepository"/>.</summary>
public sealed class LearnedFieldMapRepositoryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static LearnedFieldMap NewMap(string host = "boards.greenhouse.io", string formSignature = "sig-1") => new()
    {
        Host = host,
        FormSignature = formSignature,
        ElementToKey = new Dictionary<string, string> { ["el-1"] = "firstName", ["el-2"] = "lastName" },
        LearnedAt = DateTimeOffset.UnixEpoch,
        HitCount = 0,
    };

    [Fact]
    public async Task SaveAsync_then_GetAsync_round_trips_the_element_map()
    {
        var repository = new LearnedFieldMapRepository(_fixture.Context);
        await repository.SaveAsync(NewMap(), CancellationToken.None);

        var reloaded = await new LearnedFieldMapRepository(_fixture.Reload()).GetAsync("boards.greenhouse.io", "sig-1", CancellationToken.None);

        reloaded.ShouldNotBeNull();
        reloaded.ElementToKey["el-1"].ShouldBe("firstName");
        reloaded.ElementToKey["el-2"].ShouldBe("lastName");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_host_and_signature_pair()
    {
        (await new LearnedFieldMapRepository(_fixture.Context).GetAsync("nope.example.com", "sig-x", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_host_with_a_different_form_signature_is_a_distinct_record()
    {
        var repository = new LearnedFieldMapRepository(_fixture.Context);
        await repository.SaveAsync(NewMap(formSignature: "sig-1"), CancellationToken.None);
        await repository.SaveAsync(NewMap(formSignature: "sig-2"), CancellationToken.None);

        var context = _fixture.Reload();
        var first = await new LearnedFieldMapRepository(context).GetAsync("boards.greenhouse.io", "sig-1", CancellationToken.None);
        var second = await new LearnedFieldMapRepository(context).GetAsync("boards.greenhouse.io", "sig-2", CancellationToken.None);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveAsync_upserts_and_increments_hit_count()
    {
        var repository = new LearnedFieldMapRepository(_fixture.Context);
        await repository.SaveAsync(NewMap() with { HitCount = 1 }, CancellationToken.None);
        await repository.SaveAsync(NewMap() with { HitCount = 2 }, CancellationToken.None);

        var reloaded = await new LearnedFieldMapRepository(_fixture.Reload()).GetAsync("boards.greenhouse.io", "sig-1", CancellationToken.None);

        reloaded!.HitCount.ShouldBe(2);
    }
}
