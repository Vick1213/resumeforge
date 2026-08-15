using ResumeForge.Application.Analysis;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>Tests for <see cref="JobRepository"/>.</summary>
public sealed class JobRepositoryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SaveAsync_then_GetAsync_round_trips_a_job_posting()
    {
        var posting = TestData.Posting(id: "job-1", title: "Backend Engineer", company: "Acme", location: "Remote");
        var repository = new JobRepository(_fixture.Context);

        await repository.SaveAsync(posting, CancellationToken.None);
        var reloaded = await new JobRepository(_fixture.Reload()).GetAsync("job-1", CancellationToken.None);

        reloaded.ShouldNotBeNull();
        reloaded.Title.ShouldBe("Backend Engineer");
        reloaded.Company.ShouldBe("Acme");
        reloaded.Location.ShouldBe("Remote");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_job()
    {
        (await new JobRepository(_fixture.Context).GetAsync("ghost", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAnalysisAsync_then_GetAnalysisAsync_round_trips_every_collection()
    {
        var analysis = TestData.Analysis(
            jobId: "job-1",
            requirements: [TestData.Requirement("req:0", "5+ years C#")],
            keywords: ["csharp", "kubernetes"],
            matchedSkills: ["csharp"],
            missingSkills: ["some-niche-tool"],
            seniority: SeniorityLevel.Senior);

        var repository = new JobRepository(_fixture.Context);
        await repository.SaveAnalysisAsync(analysis, CancellationToken.None);

        var reloaded = await new JobRepository(_fixture.Reload()).GetAnalysisAsync("job-1", CancellationToken.None);

        reloaded.ShouldNotBeNull();
        reloaded.Seniority.ShouldBe(SeniorityLevel.Senior);
        reloaded.Requirements.ShouldHaveSingleItem().Text.ShouldBe("5+ years C#");
        reloaded.Keywords.ShouldBe(["csharp", "kubernetes"]);
        reloaded.MatchedSkills.ShouldBe(["csharp"]);
        reloaded.MissingSkills.ShouldBe(["some-niche-tool"]);
    }

    [Fact]
    public async Task SaveAnalysisAsync_upserts_the_analysis_for_the_same_job()
    {
        var repository = new JobRepository(_fixture.Context);
        await repository.SaveAnalysisAsync(TestData.Analysis(jobId: "job-1", keywords: ["a"]), CancellationToken.None);
        await repository.SaveAnalysisAsync(TestData.Analysis(jobId: "job-1", keywords: ["a", "b"]), CancellationToken.None);

        var reloaded = await new JobRepository(_fixture.Reload()).GetAnalysisAsync("job-1", CancellationToken.None);

        reloaded!.Keywords.ShouldBe(["a", "b"]);
    }
}
