namespace ResumeForge.Application.Tailoring;

/// <summary>Orchestrates one full tailoring run by building and executing the tailoring graph.</summary>
public interface ITailoringService
{
    /// <summary>Runs the tailoring pipeline for <paramref name="request"/>.</summary>
    Task<TailoringResult> TailorAsync(TailoringRequest request, CancellationToken ct);
}
