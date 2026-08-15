using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>Builds the base, untailored <see cref="ResumeDocument"/> from a knowledge-base snapshot.</summary>
public interface IResumeBuilder
{
    /// <summary>
    /// Builds a resume from every item in <paramref name="snapshot"/>. Deterministic: the
    /// same snapshot always produces entries in the same order and bullet ids that survive
    /// round-tripping through <see cref="Domain.Ids.EntityId.Parse(string)"/>.
    /// </summary>
    ResumeDocument Build(KnowledgeBaseSnapshot snapshot, string? id = null, string name = "Base resume");
}
