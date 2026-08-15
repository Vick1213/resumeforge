using TailoringGraph = ResumeForge.Application.Graph.Graph;

namespace ResumeForge.Application.Tailoring;

/// <summary>Declares the tailoring pipeline as a <see cref="ResumeForge.Application.Graph.Graph"/>, per CONTRACTS.md §7.</summary>
public interface ITailoringGraphFactory
{
    /// <summary>Builds the graph for one tailoring run against <paramref name="request"/>.</summary>
    TailoringGraph Create(TailoringRequest request);
}
