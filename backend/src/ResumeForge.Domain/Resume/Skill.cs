namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single skill within a <see cref="SkillGroup"/>.
/// </summary>
public sealed record Skill
{
    /// <summary>The skill's id, e.g. <c>"skl:languages#csharp"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display form, e.g. "C#".</summary>
    public required string Name { get; init; }

    /// <summary>Normalized match form, e.g. "csharp".</summary>
    public required string Normalized { get; init; }

    /// <summary>Whether this skill should be visually emphasized when rendered.</summary>
    public bool Emphasized { get; init; }
}
