namespace ResumeForge.Domain.Resume;

/// <summary>
/// A labeled group of related skills, e.g. "Languages" or "Cloud &amp; Infrastructure".
/// </summary>
public sealed record SkillGroup
{
    /// <summary>The group's id, e.g. <c>"skl:languages"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display label for the group.</summary>
    public required string Label { get; init; }

    /// <summary>Skills belonging to this group.</summary>
    public required IReadOnlyList<Skill> Items { get; init; }

    /// <summary>Whether this group is included in the rendered resume.</summary>
    public bool Included { get; init; } = true;
}
