using System.Text.Json.Serialization;

namespace ResumeForge.Infrastructure.GitHub;

/// <summary>Deserialization shape of a single repository from the GitHub REST API.</summary>
internal sealed class GitHubRepositoryDto
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    public string? Homepage { get; set; }

    public string? Language { get; set; }

    public List<string> Topics { get; set; } = [];

    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("pushed_at")]
    public DateTimeOffset PushedAt { get; set; }

    public bool Fork { get; set; }
}
