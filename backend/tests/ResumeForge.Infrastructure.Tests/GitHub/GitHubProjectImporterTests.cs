using System.Net;
using System.Text;
using System.Text.Json;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.GitHub;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.GitHub;

/// <summary>
/// Tests for <see cref="GitHubProjectImporter"/>: fixture-JSON mapping logic (no network)
/// and the never-clobber-a-manual-file rule.
/// </summary>
public sealed class GitHubProjectImporterTests
{
    private static readonly JsonSerializerOptions DtoOptions = new() { PropertyNameCaseInsensitive = true };

    private const string FlowmeshJson = """
        {
          "name": "flowmesh",
          "description": "A distributed task queue with backpressure-aware priority lanes",
          "html_url": "https://github.com/example/flowmesh",
          "homepage": "https://flowmesh.dev",
          "language": "Go",
          "topics": ["distributed-systems", "infrastructure"],
          "stargazers_count": 1140,
          "created_at": "2023-01-15T00:00:00Z",
          "pushed_at": "2024-06-01T00:00:00Z",
          "fork": false
        }
        """;

    [Fact]
    public void ToKnowledgeItem_maps_every_documented_field()
    {
        var dto = JsonSerializer.Deserialize<GitHubRepositoryDto>(FlowmeshJson, DtoOptions)!;

        var item = GitHubProjectImporter.ToKnowledgeItem(dto, "flowmesh");

        item.Id.ToString().ShouldBe("prj:flowmesh");
        item.Type.ShouldBe(KnowledgeItemType.Project);
        item.Title.ShouldBe("flowmesh");
        item.Source.ShouldBe(KnowledgeSource.GitHub);
        item.Extra["repoUrl"].ShouldBe("https://github.com/example/flowmesh");
        item.Extra["url"].ShouldBe("https://flowmesh.dev");
        item.Extra["tagline"].ShouldBe("A distributed task queue with backpressure-aware priority lanes");
        item.Extra["stars"].ShouldBe("1140");
        item.Tech.ShouldBe(["Go", "distributed-systems", "infrastructure"]);
        item.StartDate.ShouldBe(new DateOnly(2023, 1, 15));
        item.EndDate.ShouldBe(new DateOnly(2024, 6, 1));
    }

    [Fact]
    public void ToKnowledgeItem_generates_factual_bullets_from_description_and_topics_only()
    {
        var dto = JsonSerializer.Deserialize<GitHubRepositoryDto>(FlowmeshJson, DtoOptions)!;

        var item = GitHubProjectImporter.ToKnowledgeItem(dto, "flowmesh");

        item.Bullets.Count.ShouldBe(2);
        item.Bullets[0].Text.ShouldBe("A distributed task queue with backpressure-aware priority lanes");
        item.Bullets[1].Text.ShouldBe("Topics: distributed-systems, infrastructure");
        item.Bullets.ShouldAllBe(b => b.Variants.Count == 0);
    }

    [Fact]
    public void ToKnowledgeItem_never_invents_metrics_the_bullets_only_restate_github_fields()
    {
        var dto = JsonSerializer.Deserialize<GitHubRepositoryDto>(FlowmeshJson, DtoOptions)!;

        var item = GitHubProjectImporter.ToKnowledgeItem(dto, "flowmesh");

        // "1140" (the star count) must never be woven into bullet prose as an invented claim;
        // it only ever appears in the frontmatter-only Extra["stars"] field.
        item.Bullets.ShouldAllBe(b => !b.Text.Contains("1140", StringComparison.Ordinal));
    }

    [Fact]
    public void ToKnowledgeItem_omits_optional_extra_fields_when_absent()
    {
        const string json = """
            {
              "name": "tinyorm",
              "description": null,
              "html_url": "https://github.com/example/tinyorm",
              "homepage": null,
              "language": "C#",
              "topics": [],
              "stargazers_count": 0,
              "created_at": "2020-08-01T00:00:00Z",
              "pushed_at": "2021-03-01T00:00:00Z",
              "fork": false
            }
            """;
        var dto = JsonSerializer.Deserialize<GitHubRepositoryDto>(json, DtoOptions)!;

        var item = GitHubProjectImporter.ToKnowledgeItem(dto, "tinyorm");

        item.Extra.ShouldNotContainKey("url");
        item.Extra.ShouldNotContainKey("tagline");
        item.Bullets.ShouldBeEmpty();
        item.Tech.ShouldBe(["C#"]);
    }

    [Fact]
    public async Task ImportAsync_never_overwrites_a_file_marked_source_manual()
    {
        var reader = Substitute.For<IKnowledgeBaseReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [TestData.KnowledgeItem(KnowledgeItemType.Project, "flowmesh", "Flowmesh", source: KnowledgeSource.Manual)],
            Basics = new ResumeBasics { FullName = "Jordan Rivera" },
            Diagnostics = [],
        });

        var writer = Substitute.For<IKnowledgeBaseWriter>();
        var importer = NewImporter(reader, writer, FlowmeshJson);

        var result = await importer.ImportAsync("example", ["flowmesh"], CancellationToken.None);

        result.Skipped.ShouldBe(["flowmesh"]);
        result.Imported.ShouldBeEmpty();
        await writer.DidNotReceive().WriteAsync(Arg.Any<KnowledgeItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_overwrites_an_existing_github_sourced_file()
    {
        var reader = Substitute.For<IKnowledgeBaseReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [TestData.KnowledgeItem(KnowledgeItemType.Project, "flowmesh", "Flowmesh (stale)", source: KnowledgeSource.GitHub)],
            Basics = new ResumeBasics { FullName = "Jordan Rivera" },
            Diagnostics = [],
        });

        var writer = Substitute.For<IKnowledgeBaseWriter>();
        var importer = NewImporter(reader, writer, FlowmeshJson);

        var result = await importer.ImportAsync("example", ["flowmesh"], CancellationToken.None);

        result.Imported.ShouldBe(["flowmesh"]);
        result.Skipped.ShouldBeEmpty();
        await writer.Received(1).WriteAsync(Arg.Is<KnowledgeItem>(i => i.Title == "flowmesh"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_writes_a_brand_new_project_that_has_no_existing_file()
    {
        var reader = Substitute.For<IKnowledgeBaseReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [],
            Basics = new ResumeBasics { FullName = "Jordan Rivera" },
            Diagnostics = [],
        });

        var writer = Substitute.For<IKnowledgeBaseWriter>();
        var importer = NewImporter(reader, writer, FlowmeshJson);

        var result = await importer.ImportAsync("example", ["flowmesh"], CancellationToken.None);

        result.Imported.ShouldBe(["flowmesh"]);
    }

    [Fact]
    public async Task ListRepositoriesAsync_excludes_forks_and_maps_summary_fields()
    {
        const string listJson = """
            [
              { "name": "flowmesh", "description": "desc", "html_url": "https://github.com/example/flowmesh",
                "topics": ["go"], "stargazers_count": 10, "created_at": "2023-01-01T00:00:00Z", "pushed_at": "2023-02-01T00:00:00Z", "fork": false },
              { "name": "a-fork", "description": "forked repo", "html_url": "https://github.com/example/a-fork",
                "topics": [], "stargazers_count": 0, "created_at": "2023-01-01T00:00:00Z", "pushed_at": "2023-02-01T00:00:00Z", "fork": true }
            ]
            """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listJson, Encoding.UTF8, "application/json") });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var importer = new GitHubProjectImporter(new StubHttpClientFactory(client), Substitute.For<IKnowledgeBaseReader>(), Substitute.For<IKnowledgeBaseWriter>());

        var repos = await importer.ListRepositoriesAsync("example", CancellationToken.None);

        repos.ShouldHaveSingleItem().Name.ShouldBe("flowmesh");
    }

    [Fact]
    public async Task A_non_success_github_response_throws_a_catchable_exception()
    {
        var reader = Substitute.For<IKnowledgeBaseReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [],
            Basics = new ResumeBasics { FullName = "Jordan Rivera" },
            Diagnostics = [],
        });

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var importer = new GitHubProjectImporter(new StubHttpClientFactory(client), reader, Substitute.For<IKnowledgeBaseWriter>());

        await Should.ThrowAsync<GitHubApiException>(() => importer.ImportAsync("example", ["ghost-repo"], CancellationToken.None));
    }

    private static GitHubProjectImporter NewImporter(IKnowledgeBaseReader reader, IKnowledgeBaseWriter writer, string repoJson)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(repoJson, Encoding.UTF8, "application/json") });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        return new GitHubProjectImporter(new StubHttpClientFactory(client), reader, writer);
    }
}
