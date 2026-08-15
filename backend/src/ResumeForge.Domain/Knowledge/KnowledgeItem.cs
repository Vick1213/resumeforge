using ResumeForge.Domain.Ids;

namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// A single parsed knowledge-base markdown file: one experience, project, education
/// entry, certification, or the basics profile.
/// </summary>
public sealed record KnowledgeItem
{
    /// <summary>The addressable id derived from this item's file, e.g. <c>exp:acme-corp</c>.</summary>
    public required EntityId Id { get; init; }

    /// <summary>The item's category.</summary>
    public required KnowledgeItemType Type { get; init; }

    /// <summary>The slug derived from the filename (without extension).</summary>
    public required string Slug { get; init; }

    /// <summary>Path of the source markdown file, relative to the profile root.</summary>
    public required string FilePath { get; init; }

    /// <summary>Display title: role, project name, credential, or certification name.</summary>
    public required string Title { get; init; }

    /// <summary>Organization, employer, or issuer, where applicable.</summary>
    public string? Organization { get; init; }

    /// <summary>Start date parsed from frontmatter, if given.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>End date parsed from frontmatter, if given.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>True when frontmatter declared this item ongoing (<c>present</c>/<c>current</c>).</summary>
    public bool IsCurrent { get; init; }

    /// <summary>Technologies listed in frontmatter.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>Tags listed in frontmatter.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Bullets parsed from the markdown body.</summary>
    public IReadOnlyList<KnowledgeBullet> Bullets { get; init; } = [];

    /// <summary>Frontmatter keys not otherwise mapped to a named property.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>();

    /// <summary>Where this item's content originated.</summary>
    public required KnowledgeSource Source { get; init; }

    /// <summary>The full, unparsed markdown file contents.</summary>
    public required string RawMarkdown { get; init; }
}
