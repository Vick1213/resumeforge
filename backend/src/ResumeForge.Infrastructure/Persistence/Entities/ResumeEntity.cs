using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>Resumes</c> table.</summary>
public sealed class ResumeEntity
{
    /// <summary>Primary key; matches <see cref="ResumeDocument.Id"/>.</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required ResumeBasics Basics { get; set; }

    public string? Summary { get; set; }

    public required List<SkillGroup> Skills { get; set; }

    public required List<ExperienceEntry> Experience { get; set; }

    public required List<ProjectEntry> Projects { get; set; }

    public required List<EducationEntry> Education { get; set; }

    public required List<CertificationEntry> Certifications { get; set; }

    public required List<SectionKind> SectionOrder { get; set; }

    /// <summary>
    /// True for the resume built directly from the knowledge base with no tailoring
    /// applied (<see cref="ResumeForge.Application.Tailoring.IResumeBuilder"/>'s default
    /// name of <c>"Base resume"</c>), so <c>GetBaseAsync</c> can find it without a
    /// dedicated column in the shared <see cref="ResumeDocument"/> contract.
    /// </summary>
    public required bool IsBase { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
