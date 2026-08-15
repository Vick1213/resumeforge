using Microsoft.EntityFrameworkCore;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Persistence.Entities;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests that JSON-converted collection properties have a <c>ValueComparer</c> EF can use
/// for change tracking: mutating a loaded collection in place (rather than replacing the
/// whole property) must still be detected and persisted on <c>SaveChanges</c>, and an
/// unmodified reload/save must not produce a spurious update.
/// </summary>
public sealed class JsonColumnValueComparerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static ResumeEntity NewEntity() => new()
    {
        Id = "r1",
        Name = "Base resume",
        Basics = new ResumeBasics { FullName = "Jordan Rivera" },
        Skills = [],
        Experience = [],
        Projects = [],
        Education = [],
        Certifications = [],
        SectionOrder = [SectionKind.Experience],
        IsBase = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Mutating_a_loaded_list_in_place_is_detected_and_persisted()
    {
        _fixture.Context.Resumes.Add(NewEntity());
        await _fixture.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context2 = _fixture.Reload();
        var loaded = await context2.Resumes.SingleAsync(r => r.Id == "r1", TestContext.Current.CancellationToken);
        loaded.SectionOrder.Add(SectionKind.Skills);

        var writes = await context2.SaveChangesAsync(TestContext.Current.CancellationToken);
        writes.ShouldBe(1);

        var context3 = _fixture.Reload();
        var reloaded = await context3.Resumes.SingleAsync(r => r.Id == "r1", TestContext.Current.CancellationToken);
        reloaded.SectionOrder.ShouldBe([SectionKind.Experience, SectionKind.Skills]);
    }

    [Fact]
    public async Task Saving_again_without_any_mutation_writes_nothing()
    {
        _fixture.Context.Resumes.Add(NewEntity());
        await _fixture.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context2 = _fixture.Reload();
        await context2.Resumes.SingleAsync(r => r.Id == "r1", TestContext.Current.CancellationToken);

        var writes = await context2.SaveChangesAsync(TestContext.Current.CancellationToken);
        writes.ShouldBe(0);
    }

    [Fact]
    public async Task Mutating_a_nested_list_element_collection_is_detected()
    {
        var entity = NewEntity();
        entity.Experience =
        [
            new ExperienceEntry
            {
                Id = "exp:acme",
                Role = "Engineer",
                Organization = "Acme",
                StartDate = new DateOnly(2020, 1, 1),
                Bullets = [new Bullet { Id = "exp:acme#0", Text = "Original bullet." }],
            },
        ];

        _fixture.Context.Resumes.Add(entity);
        await _fixture.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context2 = _fixture.Reload();
        var loaded = await context2.Resumes.SingleAsync(r => r.Id == "r1", TestContext.Current.CancellationToken);

        // Replace the whole Experience list with one whose nested Bullets differ, simulating
        // what ResumeRepository does on save: this must be recognized as a change even
        // though the outer List<ExperienceEntry> reference itself is also new.
        loaded.Experience =
        [
            loaded.Experience[0] with
            {
                Bullets = [new Bullet { Id = "exp:acme#0", Text = "Original bullet." }, new Bullet { Id = "exp:acme#1", Text = "New bullet." }],
            },
        ];

        var writes = await context2.SaveChangesAsync(TestContext.Current.CancellationToken);
        writes.ShouldBe(1);

        var context3 = _fixture.Reload();
        var reloaded = await context3.Resumes.SingleAsync(r => r.Id == "r1", TestContext.Current.CancellationToken);
        reloaded.Experience.ShouldHaveSingleItem().Bullets.Count.ShouldBe(2);
    }
}
