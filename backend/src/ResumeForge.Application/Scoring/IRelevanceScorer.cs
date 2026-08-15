using ResumeForge.Application.Analysis;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Scoring;

/// <summary>
/// Scores every bullet and skill in a resume against a job analysis, deterministically
/// and without any model call, producing the candidate set offered to the tailoring model.
/// </summary>
public interface IRelevanceScorer
{
    /// <summary>Scores every experience bullet, project bullet, and skill in <paramref name="baseResume"/>.</summary>
    CandidateSet Score(ResumeDocument baseResume, JobAnalysis analysis);
}
