using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to resume persistence. The knowledge base markdown remains the source of truth
/// for knowledge items; this port stores the derived <see cref="ResumeDocument"/>s —
/// the base resume and every tailored variant — that the API and frontend read back.
/// </summary>
public interface IResumeRepository
{
    /// <summary>Retrieves a resume by id, or null when it does not exist.</summary>
    Task<ResumeDocument?> GetAsync(string id, CancellationToken ct);

    /// <summary>Lists every stored resume, most recently updated first.</summary>
    Task<IReadOnlyList<ResumeDocument>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Retrieves the current base resume (the one built directly from the knowledge base,
    /// with no tailoring applied), or null when none has been built yet.
    /// </summary>
    Task<ResumeDocument?> GetBaseAsync(CancellationToken ct);

    /// <summary>Inserts or updates <paramref name="resume"/>.</summary>
    Task SaveAsync(ResumeDocument resume, CancellationToken ct);
}
