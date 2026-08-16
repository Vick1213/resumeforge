using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// The application-tracker funnel stage, as the frontend models it
/// (<c>frontend/src/api/types.ts</c>'s <c>ApplicationStatus</c>): six stages, serialized as
/// exactly these lowercase strings. The backend's own
/// <see cref="Application.Abstractions.ApplicationStatus"/> has only five values and splits
/// the funnel differently (one <c>Interviewing</c> stage, plus a <c>Withdrawn</c> the
/// frontend has no slot for), so the two cannot map one-to-one — see
/// <see cref="ApplicationStatusMapping"/> and the implementation report for the ruling.
/// </summary>
public enum ApplicationStatusDto
{
    /// <summary>Saved for later, not yet applied.</summary>
    Saved,

    /// <summary>Application submitted.</summary>
    Applied,

    /// <summary>Early-stage screening (recruiter call, take-home, etc.).</summary>
    Screening,

    /// <summary>In an onsite/panel interview loop.</summary>
    Interview,

    /// <summary>An offer has been extended.</summary>
    Offer,

    /// <summary>The application was rejected, or the candidate withdrew.</summary>
    Rejected,
}

/// <summary>
/// Maps between the frontend's six-stage <see cref="ApplicationStatusDto"/> and the
/// backend's five-value <see cref="Application.Abstractions.ApplicationStatus"/>. Lossy in
/// both directions: <see cref="ApplicationStatusDto.Screening"/> and
/// <see cref="ApplicationStatusDto.Interview"/> both collapse to the single backend
/// <c>Interviewing</c> value on the way in (so a saved "screening" reads back as
/// "interview"), and the backend's <c>Withdrawn</c> reads back as <c>Rejected</c> since the
/// frontend has no separate slot for it. This is a real, unresolved gap — see the
/// implementation report — not something a mapping table can fix without either project
/// changing its enum.
/// </summary>
public static class ApplicationStatusMapping
{
    /// <summary>Maps a backend status to the frontend's wire representation.</summary>
    public static ApplicationStatusDto ToDto(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Saved => ApplicationStatusDto.Saved,
        ApplicationStatus.Applied => ApplicationStatusDto.Applied,
        ApplicationStatus.Interviewing => ApplicationStatusDto.Interview,
        ApplicationStatus.Offer => ApplicationStatusDto.Offer,
        ApplicationStatus.Rejected => ApplicationStatusDto.Rejected,
        ApplicationStatus.Withdrawn => ApplicationStatusDto.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown application status."),
    };

    /// <summary>Maps the frontend's wire representation to a backend status.</summary>
    public static ApplicationStatus ToDomain(ApplicationStatusDto status) => status switch
    {
        ApplicationStatusDto.Saved => ApplicationStatus.Saved,
        ApplicationStatusDto.Applied => ApplicationStatus.Applied,
        ApplicationStatusDto.Screening => ApplicationStatus.Interviewing,
        ApplicationStatusDto.Interview => ApplicationStatus.Interviewing,
        ApplicationStatusDto.Offer => ApplicationStatus.Offer,
        ApplicationStatusDto.Rejected => ApplicationStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown application status."),
    };
}

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
    public required ApplicationStatusDto Status { get; init; }

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

    /// <summary>Initial funnel stage. Defaults to <see cref="ApplicationStatusDto.Saved"/>.</summary>
    public ApplicationStatusDto Status { get; init; } = ApplicationStatusDto.Saved;

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
    public ApplicationStatusDto? Status { get; init; }

    /// <summary>New notes, or null to leave them unchanged.</summary>
    public string? Notes { get; init; }

    /// <summary>Accepted but not persisted; see the type's own remarks.</summary>
    public DateTimeOffset? AppliedAt { get; init; }
}
