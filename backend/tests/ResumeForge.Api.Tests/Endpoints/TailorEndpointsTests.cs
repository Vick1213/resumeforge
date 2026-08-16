using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/tailor</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class TailorEndpointsTests(ResumeForgeApiFactory factory)
{
    private const string SamplePosting =
        "We are hiring a Senior Backend Engineer. Requirements: 5+ years of experience with " +
        "C# and .NET, strong PostgreSQL skills. Nice to have: Kubernetes experience.";

    [Fact]
    public async Task Dry_run_returns_a_result_without_persisting_a_new_resume()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);
        var resumeCountBefore = (await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options))!.Count;

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();
        result.Document.ShouldNotBeNull();
        result.Commands.ShouldNotBeNull();
        result.Coverage.ShouldNotBeNull();
        result.Trace.ShouldNotBeEmpty();

        var resumeCountAfter = (await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options))!.Count;
        resumeCountAfter.ShouldBe(resumeCountBefore);
    }

    [Fact]
    public async Task Non_dry_run_persists_a_new_resume_and_a_readable_trace()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var baseResponse = await client.PostAsync("/api/resumes/base", content: null);
        var baseResume = await baseResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);
        baseResume.ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = false }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldNotBeNull();
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith("/api/tailor/");
        location.ShouldEndWith("/trace");

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        // CommandExecutor (Application layer) preserves the source document's Id/Name, so
        // this endpoint re-ids and renames the persisted variant rather than overwriting
        // the base resume it started from — see the implementation report.
        result.Document.Id.ShouldNotBe(baseResume.Id);

        var runId = location["/api/tailor/".Length..^"/trace".Length];

        using var traceResponse = await client.GetAsync($"/api/tailor/{runId}/trace");
        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trace = await traceResponse.Content.ReadFromJsonAsync<List<GraphNodeTrace>>(TestJson.Options);
        trace.ShouldNotBeNull();
        trace.ShouldNotBeEmpty();

        var persisted = await client.GetFromJsonAsync<ResumeDocument>($"/api/resumes/{result.Document.Id}", TestJson.Options);
        persisted.ShouldNotBeNull();
        persisted.Id.ShouldBe(result.Document.Id);
    }

    [Fact]
    public async Task Tailor_for_unknown_job_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = "does-not-exist" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_trace_for_unknown_run_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/tailor/does-not-exist/trace");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> CreateJobAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/jobs", new CreateJobRequest { RawText = SamplePosting }, TestJson.Options);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        return posting!.Id;
    }
}
