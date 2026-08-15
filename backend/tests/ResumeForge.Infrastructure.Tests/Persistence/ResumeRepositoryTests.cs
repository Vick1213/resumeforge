using Microsoft.EntityFrameworkCore;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>Tests for <see cref="ResumeRepository"/> against a real (in-memory) SQLite database.</summary>
public sealed class ResumeRepositoryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SaveAsync_then_GetAsync_round_trips_every_field()
    {
        var bullet = TestData.Bullet("exp:acme#0", "Cut latency 8x.", variants: ["Alt phrasing."]);
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2020, 1, 1), null, bullets: [bullet], tech: ["C#"]);
        var skill = TestData.Skill("skl:languages#csharp", "C#", "csharp", emphasized: true);
        var group = TestData.SkillGroup("skl:languages", "Languages", [skill]);

        var document = TestData.Document(
            id: "resume-1", name: "Base resume", experience: [entry], skills: [group], summary: "A summary.",
            createdAt: DateTimeOffset.UnixEpoch, updatedAt: DateTimeOffset.UnixEpoch);

        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(document, CancellationToken.None);

        var reloaded = await new ResumeRepository(_fixture.Reload()).GetAsync("resume-1", CancellationToken.None);

        reloaded.ShouldNotBeNull();
        reloaded.Name.ShouldBe("Base resume");
        reloaded.Summary.ShouldBe("A summary.");
        reloaded.Experience.ShouldHaveSingleItem().Bullets.ShouldHaveSingleItem().Variants.ShouldBe(["Alt phrasing."]);
        reloaded.Skills.ShouldHaveSingleItem().Items.ShouldHaveSingleItem().Emphasized.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        var repository = new ResumeRepository(_fixture.Context);

        (await repository.GetAsync("does-not-exist", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAsync_upserts_an_existing_resume()
    {
        var repository = new ResumeRepository(_fixture.Context);
        var document = TestData.Document(id: "resume-1", name: "Base resume", summary: "First.");
        await repository.SaveAsync(document, CancellationToken.None);

        await repository.SaveAsync(document with { Summary = "Second." }, CancellationToken.None);

        var reloaded = await new ResumeRepository(_fixture.Reload()).GetAsync("resume-1", CancellationToken.None);
        reloaded!.Summary.ShouldBe("Second.");

        (await _fixture.Context.Resumes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ListAsync_orders_most_recently_updated_first()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "r1", name: "One", updatedAt: DateTimeOffset.UnixEpoch), CancellationToken.None);
        await repository.SaveAsync(TestData.Document(id: "r2", name: "Two", updatedAt: DateTimeOffset.UnixEpoch.AddDays(1)), CancellationToken.None);

        var list = await new ResumeRepository(_fixture.Reload()).ListAsync(CancellationToken.None);

        list.Select(r => r.Id).ShouldBe(["r2", "r1"]);
    }

    [Fact]
    public async Task GetBaseAsync_returns_the_resume_named_Base_resume()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "tailored-1", name: "Acme - Backend"), CancellationToken.None);
        await repository.SaveAsync(TestData.Document(id: "base-1", name: "Base resume"), CancellationToken.None);

        var baseResume = await new ResumeRepository(_fixture.Reload()).GetBaseAsync(CancellationToken.None);

        baseResume.ShouldNotBeNull();
        baseResume.Id.ShouldBe("base-1");
    }

    [Fact]
    public async Task GetBaseAsync_returns_null_when_no_base_resume_has_been_saved()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "tailored-1", name: "Acme - Backend"), CancellationToken.None);

        (await new ResumeRepository(_fixture.Reload()).GetBaseAsync(CancellationToken.None)).ShouldBeNull();
    }
}
