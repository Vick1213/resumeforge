namespace ResumeForge.Application.Tailoring;

/// <summary>A single, human-readable change a tailoring run made to a resume.</summary>
public sealed record ResumeDiffEntry
{
    /// <summary>The id of the node this change affected.</summary>
    public required string EntityId { get; init; }

    /// <summary>The kind of change.</summary>
    public required DiffKind Kind { get; init; }

    /// <summary>The value before the change, when meaningful.</summary>
    public string? Before { get; init; }

    /// <summary>The value after the change, when meaningful.</summary>
    public string? After { get; init; }

    /// <summary>The rationale carried by the command that produced this change, if any.</summary>
    public string? Rationale { get; init; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="DiffKind.KeywordsInjected"/>: the keywords that were woven in.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

/// <summary>The kind of change a <see cref="ResumeDiffEntry"/> records.</summary>
public enum DiffKind
{
    /// <summary>A node was included, or a previously excluded bullet/skill was restored.</summary>
    Included,

    /// <summary>A node was excluded.</summary>
    Excluded,

    /// <summary>A parent's children, section order, were reordered.</summary>
    Reordered,

    /// <summary>A bullet's text was replaced with model-generated prose.</summary>
    Rewritten,

    /// <summary>A bullet's text was replaced by selecting an existing knowledge-base variant.</summary>
    VariantSelected,

    /// <summary>The summary block was set.</summary>
    SummarySet,

    /// <summary>A skill was marked for visual emphasis.</summary>
    SkillEmphasized,

    /// <summary>Job-description keywords already evidenced by the knowledge base were woven into a bullet.</summary>
    KeywordsInjected,

    /// <summary>A project's one-line description was rewritten.</summary>
    TaglineSet,
}
