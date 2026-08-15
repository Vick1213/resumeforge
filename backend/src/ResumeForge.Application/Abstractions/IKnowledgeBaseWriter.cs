using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port that persists changes back to the markdown knowledge base under <c>profile/</c>.
/// Implemented in Infrastructure against the filesystem.
/// </summary>
public interface IKnowledgeBaseWriter
{
    /// <summary>Writes (creates or overwrites) the markdown file for <paramref name="item"/>.</summary>
    Task WriteAsync(KnowledgeItem item, CancellationToken ct);

    /// <summary>Deletes the markdown file addressed by <paramref name="id"/>.</summary>
    Task DeleteAsync(EntityId id, CancellationToken ct);

    /// <summary>Writes <c>profile/basics.md</c> from <paramref name="basics"/> and <paramref name="summary"/>.</summary>
    Task WriteBasicsAsync(ResumeBasics basics, string? summary, CancellationToken ct);
}
