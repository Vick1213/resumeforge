namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// A single bullet parsed from a knowledge-base markdown body, along with any indented
/// alternate phrasings (variants) and inline tags.
/// </summary>
public sealed record KnowledgeBullet
{
    /// <summary>The primary phrasing of the bullet.</summary>
    public required string Text { get; init; }

    /// <summary>Alternate phrasings of the same accomplishment.</summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>Tags associated with this bullet.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
