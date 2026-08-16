using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Domain.Knowledge;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for <c>/api/knowledge</c> (CONTRACTS.md §9; shapes per
/// <c>frontend/src/api/types.ts</c>, which does not define bullets/dates on the DTOs).
/// </summary>
[Collection("ResumeForgeApi")]
public sealed class KnowledgeEndpointsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task List_returns_every_fixture_item()
    {
        using var client = factory.CreateClient();

        var items = await client.GetFromJsonAsync<List<KnowledgeItemDto>>("/api/knowledge", TestJson.Options);

        items.ShouldNotBeNull();
        items.ShouldContain(i => i.Id == "exp:acme-corp" && i.Kind == KnowledgeItemType.Experience);
        items.ShouldContain(i => i.Id == "prj:graph-runner" && i.Kind == KnowledgeItemType.Project);
        items.ShouldContain(i => i.Id == "edu:state-university" && i.Kind == KnowledgeItemType.Education);
        items.ShouldContain(i => i.Id == "cert:az-204" && i.Kind == KnowledgeItemType.Certification);

        var experience = items.Single(i => i.Id == "exp:acme-corp");
        experience.Subtitle.ShouldNotBeNull().ShouldContain("Acme Corp");
        experience.Tech.ShouldContain("C#");
    }

    [Fact]
    public async Task Get_by_id_returns_detail_with_raw_markdown()
    {
        using var client = factory.CreateClient();

        var item = await client.GetFromJsonAsync<KnowledgeItemDetailDto>("/api/knowledge/exp:acme-corp", TestJson.Options);

        item.ShouldNotBeNull();
        item.Title.ShouldBe("Senior Backend Engineer");
        item.Subtitle.ShouldNotBeNull().ShouldContain("Acme Corp");
        item.RawMarkdown.ShouldContain("Acme Corp");
        item.RawMarkdown.ShouldContain("5x");
    }

    [Fact]
    public async Task Get_unknown_id_returns_404_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/knowledge/exp:does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Put_writes_the_raw_markdown_verbatim_and_delete_removes_it()
    {
        using var client = factory.CreateClient();
        const string id = "prj:api-tests-scratch-project";
        const string markdown = """
            ---
            type: project
            name: Scratch Project
            tech: [Go]
            ---

            - Did a thing.
            """;

        var request = new UpsertKnowledgeRequest { RawMarkdown = markdown };

        using var putResponse = await client.PutAsJsonAsync($"/api/knowledge/{id}", request, TestJson.Options);
        putResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await putResponse.Content.ReadFromJsonAsync<KnowledgeItemDetailDto>(TestJson.Options);
        created.ShouldNotBeNull();
        created.Id.ShouldBe(id);
        created.Title.ShouldBe("Scratch Project");
        created.RawMarkdown.ShouldBe(markdown);

        using var deleteResponse = await client.DeleteAsync($"/api/knowledge/{id}");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getAfterDelete = await client.GetAsync($"/api/knowledge/{id}");
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_with_a_non_file_kind_id_returns_400()
    {
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/knowledge/sum", new UpsertKnowledgeRequest { RawMarkdown = "x" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_github_preview_lists_repositories_without_committing()
    {
        using var client = factory.CreateClient();

        var request = new GitHubImportRequest { Username = "test-candidate", RepositoryNames = [], Commit = false };

        using var response = await client.PostAsJsonAsync("/api/knowledge/import/github", request, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GitHubImportResult>(TestJson.Options);
        result.ShouldNotBeNull();
        result.Repos.ShouldContain(r => r.RepoName == ResumeForgeApiFactory.StubGitHubRepoName);
        result.Repos.Single().GeneratedMarkdown.ShouldContain("stub repository");
        result.Committed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_github_with_commit_writes_the_repository_and_can_be_deleted()
    {
        using var client = factory.CreateClient();
        var id = $"prj:{ResumeForgeApiFactory.StubGitHubRepoName}";

        try
        {
            var request = new GitHubImportRequest
            {
                Username = "test-candidate",
                RepositoryNames = [ResumeForgeApiFactory.StubGitHubRepoName],
                Commit = true,
            };

            using var response = await client.PostAsJsonAsync("/api/knowledge/import/github", request, TestJson.Options);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<GitHubImportResult>(TestJson.Options);
            result.ShouldNotBeNull();
            result.Committed.ShouldContain(ResumeForgeApiFactory.StubGitHubRepoName);

            using var getResponse = await client.GetAsync($"/api/knowledge/{id}");
            getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await client.DeleteAsync($"/api/knowledge/{id}");
        }
    }
}
