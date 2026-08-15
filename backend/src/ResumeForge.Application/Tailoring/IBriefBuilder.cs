using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Builds the compact text brief sent to the language model — the entire token-economics
/// story of the product. The brief contains only requirement ids/text/mandatory flags and
/// candidate ids/truncated text/available variant counts; it never includes the full
/// resume, the full job description, or any prose the model does not strictly need.
/// </summary>
public interface IBriefBuilder
{
    /// <summary>Builds the brief for one tailoring run.</summary>
    string Build(JobAnalysis analysis, CandidateSet candidates, ResumeDocument baseResume, TailorOptions options);

    /// <summary>
    /// Estimates the token count of <paramref name="text"/> using a simple
    /// characters-divided-by-four heuristic. Approximate — not a real tokenizer.
    /// </summary>
    int EstimateTokens(string text);
}
