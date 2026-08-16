using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// Wire projection of a <see cref="JobApplicationRecord"/> for the <c>/api/applications</c>
/// routes, joined with its <see cref="JobPosting"/> for the company/title/location
/// fields <c>JobApplicationRecord</c> does not itself store. CONTRACTS.md §9 names this type
/// but does not define its shape; per the frontend integration ruling,
/// <c>frontend/src/api/types.ts</c> is authoritative for shapes CONTRACTS.md leaves
/// undefined, so this mirrors its <c>ApplicationDto</c> as closely as the backing stores
/// allow — <see cref="CoverageScore"/>, <see cref="Usage"/>, and <see cref="AppliedAt"/> have
/// nowhere to be persisted (see the implementation report) and are always null.
/// </summary>
public sealed record ApplicationDto
{
    /// <summary>The record's own id.</summary>
    public required string Id { get; init; }

    /// <summary>The job posting this application is for.</summary>
    public required string JobId { get; init; }

    /// <summary>The resume submitted, if any.</summary>
    public string? ResumeId { get; init; }

    /// <summary>Hiring company, from the associated job posting.</summary>
    public required string Company { get; init; }

    /// <summary>Job title, from the associated job posting.</summary>
    public required string Title { get; init; }

    /// <summary>Current funnel stage.</summary>
    public required ApplicationStatus Status { get; init; }

    /// <summary>The job posting's source URL.</summary>
    public string? JobUrl { get; init; }

    /// <summary>Job location, from the associated job posting.</summary>
    public string? Location { get; init; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Requirement coverage score of the tailoring run that produced <see cref="ResumeId"/>.
    /// Always null: no port exists to look up a tailoring run by its output resume id. See
    /// the implementation report.
    /// </summary>
    public double? CoverageScore { get; init; }

    /// <summary>
    /// Token spend of the tailoring run that produced <see cref="ResumeId"/>. Always null,
    /// for the same reason as <see cref="CoverageScore"/>.
    /// </summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>
    /// When the application was submitted. Always null: <c>JobApplicationRecord</c> has no
    /// column to distinguish "the moment status became Applied" from any other update. See
    /// the implementation report.
    /// </summary>
    public DateTimeOffset? AppliedAt { get; init; }

    /// <summary>When this application was first tracked.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this application was last updated.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/applications</c>. <see cref="Company"/>,
/// <see cref="Title"/>, <see cref="JobUrl"/>, and <see cref="Location"/> are folded into the
/// associated <see cref="JobPosting"/> (which does have columns for them) rather
/// than dropped, since <c>JobApplicationRecord</c> itself has none. <see cref="CoverageScore"/>
/// and <see cref="Usage"/> are accepted but cannot be persisted anywhere and are dropped —
/// see the implementation report.
/// </summary>
public sealed record CreateApplicationRequest
{
    /// <summary>The job posting this application is for. Must already exist.</summary>
    public required string JobId { get; init; }

    /// <summary>The resume submitted, if any.</summary>
    public string? ResumeId { get; init; }

    /// <summary>Hiring company. Written back onto the associated job posting.</summary>
    public required string Company { get; init; }

    /// <summary>Job title. Written back onto the associated job posting.</summary>
    public required string Title { get; init; }

    /// <summary>Initial funnel stage. Defaults to <see cref="ApplicationStatus.Saved"/>.</summary>
    public ApplicationStatus Status { get; init; } = ApplicationStatus.Saved;

    /// <summary>The job's URL. Written back onto the associated job posting's source URL.</summary>
    public string? JobUrl { get; init; }

    /// <summary>Job location. Written back onto the associated job posting.</summary>
    public string? Location { get; init; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; init; }

    /// <summary>Accepted but not persisted; see the type's own remarks.</summary>
    public double? CoverageScore { get; init; }

    /// <summary>Accepted but not persisted; see the type's own remarks.</summary>
    public TokenUsage? Usage { get; init; }
}

/// <summary>
/// Request body for <c>PATCH /api/applications/{{id}}</c>. Every property is optional and a
/// null value is treated as "leave unchanged" — plain nullable properties cannot
/// distinguish an omitted field from an explicit null, so this endpoint cannot clear
/// <see cref="Notes"/> back to null once set. <see cref="AppliedAt"/> is accepted but not
/// persisted (see <see cref="ApplicationDto.AppliedAt"/>). See the implementation report.
/// </summary>
public sealed record UpdateApplicationRequest
{
    /// <summary>New funnel stage, or null to leave it unchanged.</summary>
    public ApplicationStatus? Status { get; init; }

    /// <summary>New notes, or null to leave them unchanged.</summary>
    public string? Notes { get; init; }

    /// <summary>Accepted but not persisted; see the type's own remarks.</summary>
    public DateTimeOffset? AppliedAt { get; init; }
}
