namespace ResumeForge.Infrastructure.Skills;

/// <summary>Deserialization shape of the embedded <c>Data/skills.json</c> taxonomy document.</summary>
internal sealed class SkillsTaxonomyDocument
{
    public int Version { get; set; }

    public List<SkillsTaxonomyCategory> Categories { get; set; } = [];
}

/// <summary>A single taxonomy category, e.g. <c>languages</c>.</summary>
internal sealed class SkillsTaxonomyCategory
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public List<SkillsTaxonomyEntry> Skills { get; set; } = [];
}

/// <summary>A single canonical skill and its known alias spellings.</summary>
internal sealed class SkillsTaxonomyEntry
{
    public string Canonical { get; set; } = string.Empty;

    public string Display { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];
}
