using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/profile</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class ProfileEndpointsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Get_profile_returns_basics_and_knowledge_counts()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/profile");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ProfileDto>(TestJson.Options);

        dto.ShouldNotBeNull();
        dto.Basics.FullName.ShouldBe("Test Candidate");
        dto.Counts.Experience.ShouldBe(1);
        dto.Counts.Projects.ShouldBe(1);
        dto.Counts.Education.ShouldBe(1);
        dto.Counts.Certifications.ShouldBe(1);
        dto.Counts.Skills.ShouldBeGreaterThan(0); // derived from the fixture's tech: frontmatter
        dto.HasApiKey.ShouldBeFalse(); // ANTHROPIC_API_KEY is cleared for the whole test run
        dto.Model.ShouldBe("heuristic-v1");
    }

    [Fact]
    public async Task Put_basics_persists_the_update_and_is_restored_afterward()
    {
        using var client = factory.CreateClient();

        var original = await client.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        original.ShouldNotBeNull();

        var updated = original.Basics with { Headline = "Updated Headline For Test" };

        try
        {
            using var putResponse = await client.PutAsJsonAsync("/api/profile/basics", updated, TestJson.Options);
            putResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var putBody = await putResponse.Content.ReadFromJsonAsync<ResumeBasics>(TestJson.Options);
            putBody.ShouldNotBeNull();
            putBody.Headline.ShouldBe("Updated Headline For Test");

            var reread = await client.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
            reread.ShouldNotBeNull();
            reread.Basics.Headline.ShouldBe("Updated Headline For Test");
        }
        finally
        {
            await client.PutAsJsonAsync("/api/profile/basics", original.Basics, TestJson.Options);
        }
    }
}
