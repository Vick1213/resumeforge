namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single accomplishment line within an experience or project entry, addressable by
/// its own id (e.g. <c>"exp:acme-corp#0"</c>).
/// </summary>
public sealed record Bullet
{
    /// <summary>The bullet's own id, e.g. <c>"exp:acme-corp#0"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The variant currently selected for display.</summary>
    public required string Text { get; init; }

    /// <summary>Alternate phrasings of the same accomplishment, sourced from the knowledge base.</summary>
    public IReadOnlyList<string> Variants { get; init; } = [];

    /// <summary>Normalized skill tags associated with this bullet.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Relevance to the current tailoring target, 0..1. Zero when unscored.</summary>
    public double Relevance { get; init; }
}
