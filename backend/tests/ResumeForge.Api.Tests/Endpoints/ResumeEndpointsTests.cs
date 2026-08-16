using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/resumes</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class ResumeEndpointsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Rebuild_base_then_get_and_list_it()
    {
        using var client = factory.CreateClient();

        using var rebuildResponse = await client.PostAsync("/api/resumes/base", content: null);
        rebuildResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rebuilt = await rebuildResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);
        rebuilt.ShouldNotBeNull();
        rebuilt.Name.ShouldBe("Base resume");
        rebuilt.Experience.ShouldContain(e => e.Id == "exp:acme-corp");

        var fetched = await client.GetFromJsonAsync<ResumeDocument>($"/api/resumes/{rebuilt.Id}", TestJson.Options);
        fetched.ShouldNotBeNull();
        fetched.Id.ShouldBe(rebuilt.Id);

        var list = await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options);
        list.ShouldNotBeNull();
        list.ShouldContain(r => r.Id == rebuilt.Id && r.IsBase);
    }

    [Fact]
    public async Task Rebuilding_base_twice_updates_the_same_resume_rather_than_duplicating_it()
    {
        using var client = factory.CreateClient();

        using var firstResponse = await client.PostAsync("/api/resumes/base", content: null);
        var first = await firstResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);

        using var secondResponse = await client.PostAsync("/api/resumes/base", content: null);
        var second = await secondResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);

        var list = await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options);
        list.ShouldNotBeNull();
        list.Count(r => r.Name == "Base resume").ShouldBe(1);
    }

    [Fact]
    public async Task Get_unknown_resume_returns_404_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/resumes/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
