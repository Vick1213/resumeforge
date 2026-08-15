using ResumeForge.Application.Analysis;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>Measures how well a resume's <em>included</em> content evidences a job's requirements.</summary>
public interface ICoverageAnalyzer
{
    /// <summary>Analyzes <paramref name="document"/> against <paramref name="analysis"/>.</summary>
    CoverageReport Analyze(ResumeDocument document, JobAnalysis analysis);
}
