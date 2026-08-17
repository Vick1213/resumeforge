using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Builds the compact text brief sent to the language model — the entire token-economics
/// story of the product. The brief carries requirement ids/text/mandatory flags, candidate
/// ids/truncated text/available variant counts, and the posting's own header and text
/// (bounded by <see cref="TailorOptions.PostingExcerptChars"/>) so the model can judge
/// alignment against what the employer wrote rather than only against what the extractor
/// made of it. It never includes the full resume: the asymmetry is deliberate, since the
/// resume is what the model is deciding *about* and re-sending it is the cost the whole
/// design exists to avoid.
/// </summary>
public interface IBriefBuilder
{
    /// <summary>Builds the brief for one tailoring run.</summary>
    string Build(JobPosting posting, JobAnalysis analysis, CandidateSet candidates, ResumeDocument baseResume, TailorOptions options);

    /// <summary>
    /// Estimates the token count of <paramref name="text"/> using a simple
    /// characters-divided-by-four heuristic. Approximate — not a real tokenizer.
    /// </summary>
    int EstimateTokens(string text);
}
