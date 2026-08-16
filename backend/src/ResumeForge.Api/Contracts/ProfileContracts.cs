using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// Response for <c>GET /api/profile</c>: contact/identity basics, knowledge-base item
/// counts by category, active-model diagnostics, and any parse diagnostics raised while
/// reading the knowledge base. CONTRACTS.md §9 names this type but does not define its
/// shape; per the frontend integration ruling, <c>frontend/src/api/types.ts</c> is
/// authoritative for shapes CONTRACTS.md leaves undefined, so this mirrors its
/// <c>ProfileDto</c> exactly.
/// </summary>
public sealed record ProfileDto
{
    /// <summary>Contact and identity information from <c>profile/basics.md</c>.</summary>
    public required ResumeBasics Basics { get; init; }

    /// <summary>Knowledge-base item counts by category.</summary>
    public required KnowledgeCounts Counts { get; init; }

    /// <summary>Parse warnings and errors raised while reading the knowledge base.</summary>
    public required IReadOnlyList<KnowledgeBaseDiagnostic> Diagnostics { get; init; }

    /// <summary>
    /// True when the language model in effect is a real, key-backed provider client rather
    /// than the no-network heuristic fallback. Bound to <c>ILanguageModel.ModelId</c> at
    /// request time rather than to any one provider's environment variable name, since
    /// provider selection is configurable (CONTRACTS.md §8).
    /// </summary>
    public required bool HasApiKey { get; init; }

    /// <summary>The active language model's id, e.g. <c>"heuristic-v1"</c> or a real provider's model id.</summary>
    public required string Model { get; init; }
}

/// <summary>Knowledge-base item counts by category, as reported in <see cref="ProfileDto"/>.</summary>
public sealed record KnowledgeCounts
{
    /// <summary>Number of experience entries.</summary>
    public required int Experience { get; init; }

    /// <summary>Number of project entries.</summary>
    public required int Projects { get; init; }

    /// <summary>Number of education entries.</summary>
    public required int Education { get; init; }

    /// <summary>Number of certification entries.</summary>
    public required int Certifications { get; init; }

    /// <summary>
    /// Number of derived skill groups the base resume would currently render. Skills are
    /// synthesized from experience/project <c>tech:</c> frontmatter (CONTRACTS.md §3), so
    /// this counts <c>IResumeBuilder.Build(snapshot).Skills</c> rather than a knowledge-base
    /// item category.
    /// </summary>
    public required int Skills { get; init; }
}
