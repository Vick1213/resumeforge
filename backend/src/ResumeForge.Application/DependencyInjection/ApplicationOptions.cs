using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;

namespace ResumeForge.Application.DependencyInjection;

/// <summary>Top-level configuration surface for <see cref="ServiceCollectionExtensions.AddResumeForgeApplication"/>.</summary>
public sealed class ApplicationOptions
{
    /// <summary>Options for <see cref="GraphExecutor"/>.</summary>
    public GraphOptions Graph { get; set; } = new();

    /// <summary>Options for <see cref="Bm25RelevanceScorer"/>.</summary>
    public ScoringOptions Scoring { get; set; } = new();

    /// <summary>Options for tailoring runs.</summary>
    public TailorOptions Tailor { get; set; } = new();
}
