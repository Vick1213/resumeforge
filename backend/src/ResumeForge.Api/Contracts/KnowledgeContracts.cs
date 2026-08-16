using ResumeForge.Domain.Knowledge;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// Lightweight projection of a <see cref="KnowledgeItem"/> for <c>GET /api/knowledge</c>.
/// CONTRACTS.md §9 names this type but does not define its shape; per the frontend
/// integration ruling, <c>frontend/src/api/types.ts</c> is authoritative for shapes
/// CONTRACTS.md leaves undefined, so this mirrors its <c>KnowledgeItemDto</c> exactly.
/// </summary>
public sealed record KnowledgeItemDto
{
    /// <summary>The item's addressable id, e.g. <c>"exp:acme-corp"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The item's category.</summary>
    public required KnowledgeItemType Kind { get; init; }

    /// <summary>Display title: role, project name, credential, or certification name.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// A short, category-appropriate second line (organization and date range for
    /// experience; tagline for a project; institution for education; issuer for a
    /// certification) — there is no single field for this in <see cref="KnowledgeItem"/>,
    /// so it is composed per category.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>Technologies listed in frontmatter.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>Tags listed in frontmatter.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Where this item's content originated.</summary>
    public required KnowledgeSource Source { get; init; }

    /// <summary>
    /// The source file's last-write time. <see cref="KnowledgeItem"/> does not track this
    /// itself, so it is read from the filesystem via <c>IProfileRootProvider</c>.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Full projection of a <see cref="KnowledgeItem"/> for <c>GET /api/knowledge/{id}</c>,
/// including its raw markdown and any parse diagnostics specific to its file. Mirrors
/// <c>frontend/src/api/types.ts</c>'s <c>KnowledgeItemDetailDto</c> (which extends its
/// <c>KnowledgeItemDto</c>) as a flat shape, matching this project's convention of
/// non-inheriting <c>sealed record</c> DTOs.
/// </summary>
public sealed record KnowledgeItemDetailDto
{
    /// <summary>The item's addressable id, e.g. <c>"exp:acme-corp"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The item's category.</summary>
    public required KnowledgeItemType Kind { get; init; }

    /// <summary>Display title: role, project name, credential, or certification name.</summary>
    public required string Title { get; init; }

    /// <summary>A short, category-appropriate second line. See <see cref="KnowledgeItemDto.Subtitle"/>.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Technologies listed in frontmatter.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>Tags listed in frontmatter.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Where this item's content originated.</summary>
    public required KnowledgeSource Source { get; init; }

    /// <summary>The source file's last-write time.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The full, unparsed markdown file contents.</summary>
    public required string RawMarkdown { get; init; }

    /// <summary>Parse diagnostics raised for this item's file specifically.</summary>
    public IReadOnlyList<KnowledgeBaseDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Request body for <c>PUT /api/knowledge/{id}</c>: the complete replacement markdown file
/// content, written verbatim. The item's category and slug come from the route id.
/// </summary>
public sealed record UpsertKnowledgeRequest
{
    /// <summary>The full markdown file content (frontmatter fences and body) to persist.</summary>
    public required string RawMarkdown { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/knowledge/import/github</c>. When <see cref="Commit"/> is
/// false, this previews the given repositories (or every repository the user owns, when
/// <see cref="Repos"/> is empty) without writing anything; when true, it commits
/// them as knowledge-base project files. CONTRACTS.md §9 names this type but does not define
/// its shape; per the frontend integration ruling, <c>frontend/src/api/types.ts</c> is
/// authoritative for shapes CONTRACTS.md leaves undefined, so this mirrors its
/// <c>GitHubImportRequest</c> exactly — including naming this <see cref="Repos"/>, matching
/// <see cref="GitHubImportResult.Repos"/>, rather than the differently-named
/// <c>RepositoryNames</c> this type used to declare.
/// </summary>
public sealed record GitHubImportRequest
{
    /// <summary>The GitHub username to import repositories from.</summary>
    public required string Username { get; init; }

    /// <summary>Repository names to act on. Empty with <see cref="Commit"/> false previews everything available.</summary>
    public required IReadOnlyList<string> Repos { get; init; }

    /// <summary>When true, writes the previewed repositories to the knowledge base; when false, only previews them.</summary>
    public required bool Commit { get; init; }
}

/// <summary>A single repository's import preview, generated but not necessarily written.</summary>
public sealed record GitHubRepoPreview
{
    /// <summary>Repository name.</summary>
    public required string RepoName { get; init; }

    /// <summary>Repository description, if set.</summary>
    public string? Description { get; init; }

    /// <summary>Primary language, as detected by GitHub.</summary>
    public string? Language { get; init; }

    /// <summary>Star count.</summary>
    public required int Stars { get; init; }

    /// <summary>The repository's page on GitHub.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// The markdown that would be written for this repository. Approximates
    /// <c>GitHubProjectImporter</c>'s own generation strategy (description then topics as
    /// bullets) rather than reusing it, since that logic is private to Infrastructure and
    /// there is no port to request a dry-run render — see the implementation report.
    /// </summary>
    public required string GeneratedMarkdown { get; init; }
}

/// <summary>The outcome of one <c>POST /api/knowledge/import/github</c> call.</summary>
public sealed record GitHubImportResult
{
    /// <summary>Every repository considered, with its preview.</summary>
    public required IReadOnlyList<GitHubRepoPreview> Repos { get; init; }

    /// <summary>Slugs actually written to the knowledge base (empty unless <c>commit</c> was true).</summary>
    public required IReadOnlyList<string> Committed { get; init; }
}
