using System.Net;
using ResumeForge.Api.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for the OpenAPI document and Scalar UI (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class OpenApiAndDocsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Openapi_document_is_served_at_the_contracted_path()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"openapi\"");
        body.ShouldContain("/api/profile");
    }

    [Fact]
    public async Task Scalar_docs_ui_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/docs");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }
}
