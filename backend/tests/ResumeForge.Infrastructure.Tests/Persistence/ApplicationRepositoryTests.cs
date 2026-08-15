using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>Tests for <see cref="ApplicationRepository"/>.</summary>
public sealed class ApplicationRepositoryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static JobApplicationRecord NewApplication(string id = "", string jobId = "job-1", ApplicationStatus status = ApplicationStatus.Saved) => new()
    {
        Id = id,
        JobId = jobId,
        Status = status,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task CreateAsync_assigns_an_id_and_GetAsync_finds_it()
    {
        var repository = new ApplicationRepository(_fixture.Context);

        var created = await repository.CreateAsync(NewApplication(), CancellationToken.None);
        created.Id.ShouldNotBeNullOrWhiteSpace();

        var reloaded = await new ApplicationRepository(_fixture.Reload()).GetAsync(created.Id, CancellationToken.None);
        reloaded.ShouldNotBeNull();
        reloaded.JobId.ShouldBe("job-1");
    }

    [Fact]
    public async Task UpdateAsync_changes_status_and_notes()
    {
        var repository = new ApplicationRepository(_fixture.Context);
        var created = await repository.CreateAsync(NewApplication(), CancellationToken.None);

        var updated = await new ApplicationRepository(_fixture.Reload()).UpdateAsync(
            created with { Status = ApplicationStatus.Interviewing, Notes = "Phone screen passed." }, CancellationToken.None);

        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Interviewing);
        updated.Notes.ShouldBe("Phone screen passed.");
    }

    [Fact]
    public async Task UpdateAsync_returns_null_for_an_unknown_id()
    {
        var repository = new ApplicationRepository(_fixture.Context);

        (await repository.UpdateAsync(NewApplication(id: "does-not-exist"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_orders_most_recently_updated_first()
    {
        var repository = new ApplicationRepository(_fixture.Context);
        await repository.CreateAsync(NewApplication() with { Id = "a1", UpdatedAt = DateTimeOffset.UnixEpoch }, CancellationToken.None);
        await repository.CreateAsync(NewApplication() with { Id = "a2", UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) }, CancellationToken.None);

        var list = await new ApplicationRepository(_fixture.Reload()).ListAsync(CancellationToken.None);

        list.Select(a => a.Id).ShouldBe(["a2", "a1"]);
    }
}
