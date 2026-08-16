namespace ResumeForge.Api.Contracts;

/// <summary>
/// Lightweight projection of a stored resume for <c>GET /api/resumes</c>. CONTRACTS.md §9
/// names this type but does not define its shape; per the frontend integration ruling,
/// <c>frontend/src/api/types.ts</c> is authoritative for shapes CONTRACTS.md leaves
/// undefined, so this mirrors its <c>ResumeSummaryDto</c> exactly.
/// </summary>
public sealed record ResumeSummaryDto
{
    /// <summary>The resume's own id.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name, e.g. "Base resume" or "Acme - Backend Eng".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// True when this is the current base resume. Backed by the explicit, persisted
    /// <c>ResumeEntity.IsBase</c> flag — never derived from the resume's name — so renaming
    /// a resume never changes which one this reports as the base.
    /// </summary>
    public required bool IsBase { get; init; }

    /// <summary>When this resume was last modified.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
