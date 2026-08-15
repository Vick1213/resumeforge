namespace ResumeForge.Application.Analysis;

/// <summary>
/// Produces a deterministic, model-free <see cref="JobAnalysis"/> from a
/// <see cref="JobPosting"/>: segmentation, section detection, mandatory/optional
/// classification, keyword ranking, and seniority inference.
/// </summary>
public interface IJobAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="posting"/>. Deterministic: the same posting always
    /// produces an identical <see cref="JobAnalysis"/>, including list ordering.
    /// </summary>
    JobAnalysis Analyze(JobPosting posting);
}
