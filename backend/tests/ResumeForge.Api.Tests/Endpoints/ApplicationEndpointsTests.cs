using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for <c>/api/applications</c> (CONTRACTS.md §9; shapes per
/// <c>frontend/src/api/types.ts</c>, whose <c>ApplicationDto</c> carries fields — company,
/// title, jobUrl, location — that <c>JobApplicationRecord</c> does not itself store; see
/// the implementation report for how those are derived from the associated job posting).
/// </summary>
[Collection("ResumeForgeApi")]
public sealed class ApplicationEndpointsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Create_list_and_patch_roundtrip()
    {
        using var client = factory.CreateClient();
        var jobId = await CreateJobAsync(client);

        var createRequest = new CreateApplicationRequest
        {
            JobId = jobId,
            Company = "Acme Corp",
            Title = "Senior Backend Engineer",
            JobUrl = "https://example.com/jobs/1",
            Location = "Remote",
            Notes = "Applied via referral.",
        };
        using var createResponse = await client.PostAsJsonAsync("/api/applications", createRequest, TestJson.Options);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationDto>(TestJson.Options);
        created.ShouldNotBeNull();
        created.JobId.ShouldBe(jobId);
        created.Company.ShouldBe("Acme Corp");
        created.Title.ShouldBe("Senior Backend Engineer");
        created.JobUrl.ShouldBe("https://example.com/jobs/1");
        created.Location.ShouldBe("Remote");
        created.Status.ShouldBe(ApplicationStatus.Saved);

        var list = await client.GetFromJsonAsync<List<ApplicationDto>>("/api/applications", TestJson.Options);
        list.ShouldNotBeNull();
        list.ShouldContain(a => a.Id == created.Id && a.Company == "Acme Corp");

        var patchRequest = new UpdateApplicationRequest { Status = ApplicationStatus.Interview };
        using var patchResponse = await client.PatchAsJsonAsync($"/api/applications/{created.Id}", patchRequest, TestJson.Options);

        patchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var patched = await patchResponse.Content.ReadFromJsonAsync<ApplicationDto>(TestJson.Options);
        patched.ShouldNotBeNull();
        patched.Status.ShouldBe(ApplicationStatus.Interview);
        patched.Notes.ShouldBe("Applied via referral."); // untouched fields survive a partial patch
        patched.Company.ShouldBe("Acme Corp"); // still joined from the job posting after the patch
    }

    // Every status in the closed set (CONTRACTS.md §9) must come back unchanged: this is
    // the exact defect the old ApplicationStatusMapping bridge introduced — e.g. "screening"
    // silently reading back as "interview" after a save/reload. There is no translation
    // layer left between ApplicationStatus and the wire now, so this asserts identity for
    // all seven values, not just the two that used to collide.
    [Theory]
    [InlineData(ApplicationStatus.Saved)]
    [InlineData(ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Offer)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task Every_status_round_trips_through_create_and_a_fresh_list_read_unchanged(ApplicationStatus status)
    {
        using var client = factory.CreateClient();
        var jobId = await CreateJobAsync(client);

        var createRequest = new CreateApplicationRequest
        {
            JobId = jobId,
            Company = "Acme Corp",
            Title = "Senior Backend Engineer",
            Status = status,
        };
        using var createResponse = await client.PostAsJsonAsync("/api/applications", createRequest, TestJson.Options);
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationDto>(TestJson.Options);
        created.ShouldNotBeNull();
        created.Status.ShouldBe(status);

        // Re-read through a separate GET (the "reload" step) rather than trusting the
        // create response alone, so this also exercises the DB round-trip and the
        // enum-to-string EF conversion, not just the request/response mapping.
        var list = await client.GetFromJsonAsync<List<ApplicationDto>>("/api/applications", TestJson.Options);
        list.ShouldNotBeNull();
        list.Single(a => a.Id == created.Id).Status.ShouldBe(status);
    }

    [Fact]
    public async Task Patch_unknown_application_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.PatchAsJsonAsync(
            "/api/applications/does-not-exist", new UpdateApplicationRequest { Status = ApplicationStatus.Applied }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_for_unknown_job_returns_404()
    {
        using var client = factory.CreateClient();

        var request = new CreateApplicationRequest { JobId = "does-not-exist", Company = "Acme Corp", Title = "Engineer" };
        using var response = await client.PostAsJsonAsync("/api/applications", request, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> CreateJobAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest { RawText = "A job posting used only to satisfy the foreign id." }, TestJson.Options);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        return posting!.Id;
    }
}
