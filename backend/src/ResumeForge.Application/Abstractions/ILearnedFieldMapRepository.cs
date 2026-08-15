using ResumeForge.Application.Autofill;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to learned autofill field-map persistence, behind <c>/api/autofill/fieldmap</c>.
/// </summary>
public interface ILearnedFieldMapRepository
{
    /// <summary>Retrieves the learned map for a specific form, or null when none is stored.</summary>
    Task<LearnedFieldMap?> GetAsync(string host, string formSignature, CancellationToken ct);

    /// <summary>Inserts or updates a learned map.</summary>
    Task SaveAsync(LearnedFieldMap map, CancellationToken ct);
}
