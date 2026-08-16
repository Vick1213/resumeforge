using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Application.Analysis;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/jobs</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class JobEndpointsTests(ResumeForgeApiFactory factory)
{
    private const string SamplePosting =
        "We are hiring a Senior Backend Engineer. Requirements: 5+ years of experience with " +
        "C# and .NET, strong PostgreSQL skills. Nice to have: Kubernetes experience.";

    [Fact]
    public async Task Create_from_raw_text_then_get_its_analysis()
    {
        using var client = factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest { RawText = SamplePosting }, TestJson.Options);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var posting = await createResponse.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        posting.ShouldNotBeNull();
        posting.RawText.ShouldBe(SamplePosting);

        using var analysisResponse = await client.GetAsync($"/api/jobs/{posting.Id}/analysis");
        analysisResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var analysis = await analysisResponse.Content.ReadFromJsonAsync<JobAnalysis>(TestJson.Options);
        analysis.ShouldNotBeNull();
        analysis.JobId.ShouldBe(posting.Id);
        analysis.Requirements.ShouldNotBeNull();
        analysis.Keywords.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_from_raw_text_with_a_qualifying_first_line_extracts_the_title()
    {
        using var client = factory.CreateClient();

        const string rawText =
            "Senior Backend Engineer\n" +
            "Acme Corp is hiring a Senior Backend Engineer to join our platform team.";

        using var response = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest { RawText = rawText }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        posting.ShouldNotBeNull();
        posting.Title.ShouldBe("Senior Backend Engineer");
    }

    [Fact]
    public async Task Create_from_raw_text_with_no_qualifying_line_leaves_the_title_null()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest { RawText = SamplePosting }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        posting.ShouldNotBeNull();
        posting.Title.ShouldBeNull();
    }

    [Fact]
    public async Task Create_without_url_or_rawtext_returns_400()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/jobs", new CreateJobRequest(), TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_with_both_url_and_rawtext_returns_400()
    {
        using var client = factory.CreateClient();

        var request = new CreateJobRequest { Url = "https://example.com/job", RawText = SamplePosting };
        using var response = await client.PostAsJsonAsync("/api/jobs", request, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_analysis_for_unknown_job_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/jobs/does-not-exist/analysis");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
