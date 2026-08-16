using System.Net;
using System.Net.Http.Headers;
using ResumeForge.Api.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>
/// CORS preflight tests for the two origins CONTRACTS.md §9 requires: the Vite dev server
/// and any <c>chrome-extension://</c> origin (matched by predicate, not a literal list,
/// since an extension's origin carries a per-install random id).
/// </summary>
[Collection("ResumeForgeApi")]
public sealed class CorsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Preflight_from_the_vite_dev_origin_is_allowed()
    {
        using var client = factory.CreateClient();

        using var response = await SendPreflightAsync(client, "http://localhost:5173");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain("http://localhost:5173");
    }

    [Fact]
    public async Task Preflight_from_a_chrome_extension_origin_is_allowed()
    {
        using var client = factory.CreateClient();
        const string origin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";

        using var response = await SendPreflightAsync(client, origin);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain(origin);
    }

    [Fact]
    public async Task Preflight_from_an_unlisted_origin_is_rejected()
    {
        using var client = factory.CreateClient();

        using var response = await SendPreflightAsync(client, "https://evil.example.com");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private static async Task<HttpResponseMessage> SendPreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/profile");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        return await client.SendAsync(request);
    }
}
