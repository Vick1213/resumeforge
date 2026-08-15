using ResumeForge.Application.Graph;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence;

/// <summary>Tests for <see cref="TailoringRunRepository"/>.</summary>
public sealed class TailoringRunRepositoryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SaveAsync_returns_the_run_id_and_GetAsync_round_trips_the_full_result()
    {
        var document = TestData.Document(id: "resume-1");
        var trace = new List<GraphNodeTrace> { TestData.Trace("fetch-jd"), TestData.Trace("analyze-jd") };
        var run = new ResumeForge.Application.Abstractions.TailoringRunRecord
        {
            Id = "run-1",
            JobId = "job-1",
            BaseResumeId = "resume-1",
            Result = TestData.TailoringResult(document, trace),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        var repository = new TailoringRunRepository(_fixture.Context);
        var returnedId = await repository.SaveAsync(run, CancellationToken.None);

        returnedId.ShouldBe("run-1");

        var reloaded = await new TailoringRunRepository(_fixture.Reload()).GetAsync("run-1", CancellationToken.None);
        reloaded.ShouldNotBeNull();
        reloaded.JobId.ShouldBe("job-1");
        reloaded.Result.Document.Id.ShouldBe("resume-1");
        reloaded.Result.Trace.Select(t => t.Node).ShouldBe(["fetch-jd", "analyze-jd"]);
    }

    [Fact]
    public async Task GetTraceAsync_returns_just_the_trace_without_requiring_the_caller_to_deserialize_the_whole_result()
    {
        var document = TestData.Document(id: "resume-1");
        var trace = new List<GraphNodeTrace> { TestData.Trace("build-brief"), TestData.Trace("propose-commands", GraphNodeStatus.Failed) };
        var run = new ResumeForge.Application.Abstractions.TailoringRunRecord
        {
            Id = "run-2",
            JobId = "job-1",
            Result = TestData.TailoringResult(document, trace),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        await new TailoringRunRepository(_fixture.Context).SaveAsync(run, CancellationToken.None);

        var reloadedTrace = await new TailoringRunRepository(_fixture.Reload()).GetTraceAsync("run-2", CancellationToken.None);

        reloadedTrace.ShouldNotBeNull();
        reloadedTrace.Select(t => t.Status).ShouldBe([GraphNodeStatus.Succeeded, GraphNodeStatus.Failed]);
    }

    [Fact]
    public async Task GetTraceAsync_returns_null_for_an_unknown_run()
    {
        (await new TailoringRunRepository(_fixture.Context).GetTraceAsync("ghost", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAsync_generates_an_id_when_none_is_supplied()
    {
        var run = new ResumeForge.Application.Abstractions.TailoringRunRecord
        {
            Id = string.Empty,
            JobId = "job-1",
            Result = TestData.TailoringResult(TestData.Document()),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        var id = await new TailoringRunRepository(_fixture.Context).SaveAsync(run, CancellationToken.None);

        id.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParse(id, out _).ShouldBeTrue();
    }
}
