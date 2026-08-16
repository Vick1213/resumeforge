using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ResumeForge.Api.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>
/// Dedicated assertion that an error path returns a well-formed RFC 9457
/// <c>ProblemDetails</c> body with the correct content type (CONTRACTS.md §9: "All errors
/// use RFC 9457 ProblemDetails").
/// </summary>
[Collection("ResumeForgeApi")]
public sealed class ProblemDetailsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task A_not_found_error_is_a_well_formed_problem_details_body()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/resumes/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(404);
        problem.Title.ShouldNotBeNullOrWhiteSpace();
        problem.Type.ShouldNotBeNullOrWhiteSpace();
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
    }
}
