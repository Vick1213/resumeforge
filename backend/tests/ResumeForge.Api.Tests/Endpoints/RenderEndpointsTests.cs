using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>POST /api/render/{resumeId}</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class RenderEndpointsTests(ResumeForgeApiFactory factory)
{
    [Theory]
    [InlineData("html", "text/html")]
    [InlineData("md", "text/markdown")]
    [InlineData("pdf", "application/pdf")]
    public async Task Render_required_format_returns_a_non_empty_file(string format, string expectedContentType)
    {
        using var client = factory.CreateClient();
        var resumeId = await GetOrBuildBaseResumeIdAsync(client);

        using var response = await client.PostAsJsonAsync($"/api/render/{resumeId}", new RenderRequest { Format = format }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(expectedContentType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Render_docx_returns_501_problem_details()
    {
        using var client = factory.CreateClient();
        var resumeId = await GetOrBuildBaseResumeIdAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/render/{resumeId}", new RenderRequest { Format = "docx" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Render_unknown_resume_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/render/does-not-exist", new RenderRequest { Format = "pdf" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Render_unsupported_format_string_returns_400()
    {
        using var client = factory.CreateClient();
        var resumeId = await GetOrBuildBaseResumeIdAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/render/{resumeId}", new RenderRequest { Format = "xml" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<string> GetOrBuildBaseResumeIdAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/resumes/base", content: null);
        var resume = await response.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);
        return resume!.Id;
    }
}
