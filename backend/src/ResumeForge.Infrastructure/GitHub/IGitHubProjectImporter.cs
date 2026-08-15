namespace ResumeForge.Infrastructure.GitHub;

/// <summary>
/// Port to importing public GitHub repositories as knowledge-base project entries.
/// CONTRACTS.md does not declare this port in the Application layer (the GitHub import
/// concern is Infrastructure-specific), so it is defined here.
/// </summary>
public interface IGitHubProjectImporter
{
    /// <summary>Lists a user's public, non-fork repositories, most recently pushed first.</summary>
    Task<IReadOnlyList<GitHubRepositorySummary>> ListRepositoriesAsync(string username, CancellationToken ct);

    /// <summary>
    /// Imports the named repositories as <c>profile/projects/</c> knowledge items. A
    /// repository whose existing file has <c>source: manual</c> is never overwritten and is
    /// reported in <see cref="GitHubImportResult.Skipped"/> instead.
    /// </summary>
    Task<GitHubImportResult> ImportAsync(string username, IReadOnlyList<string> repositoryNames, CancellationToken ct);
}

/// <summary>A public GitHub repository, as summarized for the import picker.</summary>
public sealed record GitHubRepositorySummary
{
    /// <summary>Repository name.</summary>
    public required string Name { get; init; }

    /// <summary>Repository description, if set.</summary>
    public string? Description { get; init; }

    /// <summary>The repository's page on GitHub.</summary>
    public required string HtmlUrl { get; init; }

    /// <summary>The repository's configured homepage URL, if set.</summary>
    public string? Homepage { get; init; }

    /// <summary>Primary language, as detected by GitHub.</summary>
    public string? Language { get; init; }

    /// <summary>Repository topics.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Star count.</summary>
    public int Stars { get; init; }

    /// <summary>When the repository was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the repository was last pushed to.</summary>
    public DateTimeOffset PushedAt { get; init; }
}

/// <summary>The outcome of one <see cref="IGitHubProjectImporter.ImportAsync"/> call.</summary>
public sealed record GitHubImportResult
{
    /// <summary>Slugs of the project files written or overwritten.</summary>
    public required IReadOnlyList<string> Imported { get; init; }

    /// <summary>Slugs skipped because the existing file was hand-authored (<c>source: manual</c>).</summary>
    public required IReadOnlyList<string> Skipped { get; init; }
}
