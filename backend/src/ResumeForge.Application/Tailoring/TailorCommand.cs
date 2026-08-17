using System.Text.Json.Serialization;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// The model's entire output surface. The model never writes or formats a resume — it
/// receives a compact brief and returns only a list of these commands, which deterministic
/// C# validates and applies to the canonical <see cref="ResumeDocument"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(IncludeCommand), "include")]
[JsonDerivedType(typeof(ExcludeCommand), "exclude")]
[JsonDerivedType(typeof(OrderCommand), "order")]
[JsonDerivedType(typeof(SelectVariantCommand), "selectVariant")]
[JsonDerivedType(typeof(RewriteCommand), "rewrite")]
[JsonDerivedType(typeof(SetSummaryCommand), "setSummary")]
[JsonDerivedType(typeof(EmphasizeSkillsCommand), "emphasizeSkills")]
[JsonDerivedType(typeof(SetSectionOrderCommand), "setSectionOrder")]
[JsonDerivedType(typeof(InjectKeywordsCommand), "injectKeywords")]
[JsonDerivedType(typeof(SetTaglineCommand), "setTagline")]
public abstract record TailorCommand
{
    /// <summary>One short clause explaining the command, shown in the diff UI.</summary>
    public string? Rationale { get; init; }
}

/// <summary>Includes one or more nodes that would otherwise be omitted from the tailored resume.</summary>
public sealed record IncludeCommand : TailorCommand
{
    /// <summary>The node ids to include.</summary>
    public required IReadOnlyList<string> Targets { get; init; }
}

/// <summary>Excludes one or more nodes from the tailored resume.</summary>
public sealed record ExcludeCommand : TailorCommand
{
    /// <summary>The node ids to exclude.</summary>
    public required IReadOnlyList<string> Targets { get; init; }
}

/// <summary>
/// Reorders the children of a parent node. Any child omitted from <see cref="Order"/>
/// keeps its relative position, placed after every listed child.
/// </summary>
public sealed record OrderCommand : TailorCommand
{
    /// <summary>The parent whose children are being reordered, e.g. <c>"exp:acme-corp"</c>, or the literal <c>"root"</c>.</summary>
    public required string Parent { get; init; }

    /// <summary>Child ids, in the desired order.</summary>
    public required IReadOnlyList<string> Order { get; init; }
}

/// <summary>
/// Selects an existing phrasing from the knowledge base for a bullet. Costs zero
/// generation tokens and is always preferred over <see cref="RewriteCommand"/> when a
/// suitable variant exists.
/// </summary>
public sealed record SelectVariantCommand : TailorCommand
{
    /// <summary>The bullet id to update, e.g. <c>"exp:acme-corp#0"</c>.</summary>
    public required string Target { get; init; }

    /// <summary>The index into the target bullet's <see cref="Bullet.Variants"/> to select.</summary>
    public required int VariantIndex { get; init; }
}

/// <summary>
/// Replaces a bullet's text with model-generated prose. The only command that emits
/// prose; budget-capped by <see cref="TailorOptions.MaxRewrites"/>.
/// </summary>
public sealed record RewriteCommand : TailorCommand
{
    /// <summary>The bullet id to rewrite.</summary>
    public required string Target { get; init; }

    /// <summary>The replacement text.</summary>
    public required string Text { get; init; }
}

/// <summary>Sets the resume's summary block.</summary>
public sealed record SetSummaryCommand : TailorCommand
{
    /// <summary>The new summary text.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// Replaces a project's one-line description — the italic line under its name — with
/// model-generated prose. Only available at <see cref="ModelEffort.Full"/>.
/// </summary>
/// <remarks>
/// Every other prose op targets a bullet, which left the tagline the one line on the page
/// that stayed identical no matter which job was being tailored to, despite being the first
/// line a reader sees under each project. It is gated at <c>Full</c> rather than enabled
/// everywhere because it spends output tokens on a line most runs are happy to leave alone,
/// and it passes the same <see cref="IFabricationGuard"/> every rewrite does.
/// </remarks>
public sealed record SetTaglineCommand : TailorCommand
{
    /// <summary>The project entry id to retagline, e.g. <c>"prj:tinyorm"</c>.</summary>
    public required string Target { get; init; }

    /// <summary>The replacement description.</summary>
    public required string Text { get; init; }
}

/// <summary>Marks specific skills for visual emphasis when the resume is rendered.</summary>
public sealed record EmphasizeSkillsCommand : TailorCommand
{
    /// <summary>Normalized skill names to emphasize.</summary>
    public required IReadOnlyList<string> Skills { get; init; }
}

/// <summary>Sets the order sections are rendered in.</summary>
public sealed record SetSectionOrderCommand : TailorCommand
{
    /// <summary>The new section order.</summary>
    public required IReadOnlyList<SectionKind> Order { get; init; }
}

/// <summary>
/// Weaves job-description keywords into an existing bullet for keyword-matching systems.
/// Only available at <see cref="ModelEffort.Thorough"/> effort and above, and only for
/// keywords the knowledge base already evidences — see <see cref="CommandValidator"/>'s
/// rule 6. This is the honest form of ATS keyword optimization: it surfaces terms the
/// candidate can actually support and refuses the rest, never relaxing at any effort level.
/// </summary>
public sealed record InjectKeywordsCommand : TailorCommand
{
    /// <summary>The bullet id to rewrite.</summary>
    public required string Target { get; init; }

    /// <summary>Normalized keyword names woven into <see cref="Text"/>.</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>The rewritten bullet text.</summary>
    public required string Text { get; init; }
}
