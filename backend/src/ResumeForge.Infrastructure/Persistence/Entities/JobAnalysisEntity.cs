using ResumeForge.Application.Analysis;

namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>JobAnalyses</c> table; one row per job posting.</summary>
public sealed class JobAnalysisEntity
{
    /// <summary>Primary key; matches the owning <see cref="JobPosting.Id"/>.</summary>
    public required string JobId { get; set; }

    public required List<Requirement> Requirements { get; set; }

    public required List<string> Keywords { get; set; }

    public required List<string> MatchedSkills { get; set; }

    public required List<string> MissingSkills { get; set; }

    public required SeniorityLevel Seniority { get; set; }
}
