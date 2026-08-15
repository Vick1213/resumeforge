using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;

namespace ResumeForge.Infrastructure.GitHub;

/// <summary>
/// <see cref="IGitHubProjectImporter"/> over the public GitHub REST API. Regenerable
/// project files it writes carry <c>source: github</c>; a file whose frontmatter already
/// says <c>source: manual</c> is never overwritten (CONTRACTS.md §3, §7 GitHub import).
/// Generated bullets restate only what GitHub reports (the description and topic list) —
/// never a fabricated metric.
/// </summary>
public sealed class GitHubProjectImporter(
    IHttpClientFactory httpClientFactory,
    IKnowledgeBaseReader knowledgeBaseReader,
    IKnowledgeBaseWriter knowledgeBaseWriter) : IGitHubProjectImporter
{
    /// <summary>The name registered for this importer's <see cref="IHttpClientFactory"/> client.</summary>
    public const string HttpClientName = "github";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepositorySummary>> ListRepositoriesAsync(string username, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(
            $"users/{Uri.EscapeDataString(username)}/repos?per_page=100&sort=pushed&type=owner", ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new GitHubApiException(username, "*", (int)response.StatusCode, body);
        }

        var dtos = await response.Content.ReadFromJsonAsync<List<GitHubRepositoryDto>>(SerializerOptions, ct).ConfigureAwait(false) ?? [];

        return [.. dtos.Where(d => !d.Fork).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(ToSummary)];
    }

    /// <inheritdoc />
    public async Task<GitHubImportResult> ImportAsync(string username, IReadOnlyList<string> repositoryNames, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(repositoryNames);

        var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
        var existingProjectsBySlug = snapshot.Items
            .Where(i => i.Type == KnowledgeItemType.Project)
            .ToDictionary(i => i.Slug, StringComparer.Ordinal);

        var imported = new List<string>();
        var skipped = new List<string>();

        foreach (var repositoryName in repositoryNames)
        {
            ct.ThrowIfCancellationRequested();

            var repo = await FetchRepositoryAsync(username, repositoryName, ct).ConfigureAwait(false);
            var slug = Slug.From(repo.Name);

            if (existingProjectsBySlug.TryGetValue(slug, out var existing) && existing.Source == KnowledgeSource.Manual)
            {
                skipped.Add(slug);
                continue;
            }

            var item = ToKnowledgeItem(repo, slug);
            await knowledgeBaseWriter.WriteAsync(item, ct).ConfigureAwait(false);
            imported.Add(slug);
        }

        return new GitHubImportResult { Imported = imported, Skipped = skipped };
    }

    private async Task<GitHubRepositoryDto> FetchRepositoryAsync(string username, string repositoryName, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(
            $"repos/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(repositoryName)}", ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new GitHubApiException(username, repositoryName, (int)response.StatusCode, body);
        }

        return await response.Content.ReadFromJsonAsync<GitHubRepositoryDto>(SerializerOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"GitHub API returned an empty body for '{username}/{repositoryName}'.");
    }

    private static GitHubRepositorySummary ToSummary(GitHubRepositoryDto repo) => new()
    {
        Name = repo.Name,
        Description = repo.Description,
        HtmlUrl = repo.HtmlUrl,
        Homepage = repo.Homepage,
        Language = repo.Language,
        Topics = repo.Topics,
        Stars = repo.StargazersCount,
        CreatedAt = repo.CreatedAt,
        PushedAt = repo.PushedAt,
    };

    /// <summary>
    /// Maps a fetched repository to the project <see cref="KnowledgeItem"/> that would be
    /// written for it. Internal and exposed to tests via <c>InternalsVisibleTo</c> so the
    /// mapping can be verified against fixture JSON without a network call.
    /// </summary>
    internal static KnowledgeItem ToKnowledgeItem(GitHubRepositoryDto repo, string slug)
    {
        var tech = new List<string>();
        if (!string.IsNullOrWhiteSpace(repo.Language))
        {
            tech.Add(repo.Language);
        }

        foreach (var topic in repo.Topics)
        {
            if (!tech.Contains(topic, StringComparer.OrdinalIgnoreCase))
            {
                tech.Add(topic);
            }
        }

        var bullets = new List<KnowledgeBullet>();
        var description = repo.Description?.Trim();
        if (!string.IsNullOrEmpty(description))
        {
            bullets.Add(new KnowledgeBullet { Text = description, Variants = [], Tags = [] });
        }

        if (repo.Topics.Count > 0)
        {
            bullets.Add(new KnowledgeBullet { Text = $"Topics: {string.Join(", ", repo.Topics)}", Variants = [], Tags = [] });
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(description))
        {
            extra["tagline"] = description;
        }

        extra["repoUrl"] = repo.HtmlUrl;
        if (!string.IsNullOrWhiteSpace(repo.Homepage))
        {
            extra["url"] = repo.Homepage;
        }

        extra["stars"] = repo.StargazersCount.ToString(CultureInfo.InvariantCulture);

        return new KnowledgeItem
        {
            Id = EntityId.Parse($"prj:{slug}"),
            Type = KnowledgeItemType.Project,
            Slug = slug,
            FilePath = $"projects/{slug}.md",
            Title = repo.Name,
            Organization = null,
            StartDate = DateOnly.FromDateTime(repo.CreatedAt.UtcDateTime),
            EndDate = DateOnly.FromDateTime(repo.PushedAt.UtcDateTime),
            IsCurrent = false,
            Tech = tech,
            Tags = [],
            Bullets = bullets,
            Extra = extra,
            Source = KnowledgeSource.GitHub,
            RawMarkdown = string.Empty,
        };
    }
}
