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
        await repository.SaveAsync(document, isBase: true, CancellationToken.None);

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
        await repository.SaveAsync(document, isBase: true, CancellationToken.None);

        await repository.SaveAsync(document with { Summary = "Second." }, isBase: true, CancellationToken.None);

        var reloaded = await new ResumeRepository(_fixture.Reload()).GetAsync("resume-1", CancellationToken.None);
        reloaded!.Summary.ShouldBe("Second.");

        (await _fixture.Context.Resumes.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ListAsync_orders_most_recently_updated_first()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "r1", name: "One", updatedAt: DateTimeOffset.UnixEpoch), isBase: false, CancellationToken.None);
        await repository.SaveAsync(TestData.Document(id: "r2", name: "Two", updatedAt: DateTimeOffset.UnixEpoch.AddDays(1)), isBase: false, CancellationToken.None);

        var list = await new ResumeRepository(_fixture.Reload()).ListAsync(CancellationToken.None);

        list.Select(r => r.Id).ShouldBe(["r2", "r1"]);
    }

    [Fact]
    public async Task GetBaseAsync_returns_the_resume_flagged_as_base()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "tailored-1", name: "Acme - Backend"), isBase: false, CancellationToken.None);
        await repository.SaveAsync(TestData.Document(id: "base-1", name: "Base resume"), isBase: true, CancellationToken.None);

        var baseResume = await new ResumeRepository(_fixture.Reload()).GetBaseAsync(CancellationToken.None);

        baseResume.ShouldNotBeNull();
        baseResume.Id.ShouldBe("base-1");
    }

    [Fact]
    public async Task GetBaseAsync_returns_null_when_no_base_resume_has_been_saved()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "tailored-1", name: "Acme - Backend"), isBase: false, CancellationToken.None);

        (await new ResumeRepository(_fixture.Reload()).GetBaseAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_renamed_base_resume_is_still_identified_as_the_base()
    {
        // Base-ness is the isBase parameter, not the Name — a resume with no relation to
        // the literal string "Base resume" must still be found by GetBaseAsync once it is
        // saved with isBase: true, and simulates what a future rename feature would do:
        // save the same id back under a new Name.
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "base-1", name: "Base resume"), isBase: true, CancellationToken.None);

        await repository.SaveAsync(TestData.Document(id: "base-1", name: "Jordan's Main Resume"), isBase: true, CancellationToken.None);

        var baseResume = await new ResumeRepository(_fixture.Reload()).GetBaseAsync(CancellationToken.None);

        baseResume.ShouldNotBeNull();
        baseResume.Id.ShouldBe("base-1");
        baseResume.Name.ShouldBe("Jordan's Main Resume");
    }

    [Fact]
    public async Task Setting_a_new_base_clears_the_previous_one()
    {
        var repository = new ResumeRepository(_fixture.Context);
        await repository.SaveAsync(TestData.Document(id: "base-1", name: "Base resume"), isBase: true, CancellationToken.None);

        await repository.SaveAsync(TestData.Document(id: "base-2", name: "Rebuilt base"), isBase: true, CancellationToken.None);

        var reloaded = _fixture.Reload();
        var baseCount = await reloaded.Resumes.CountAsync(e => e.IsBase, TestContext.Current.CancellationToken);
        baseCount.ShouldBe(1);

        var currentBase = await new ResumeRepository(reloaded).GetBaseAsync(CancellationToken.None);
        currentBase.ShouldNotBeNull();
        currentBase.Id.ShouldBe("base-2");
    }
}
