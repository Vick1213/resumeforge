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
    /// <param name="posting">The job posting being tailored against.</param>
    /// <param name="analysis">The deterministic analysis of <paramref name="posting"/>.</param>
    /// <param name="candidates">The scored candidate pool the model chooses from.</param>
    /// <param name="baseResume">The untailored resume the run starts from.</param>
    /// <param name="options">The resolved options for this run.</param>
    /// <param name="atsReview">
    /// The <c>ats-review</c> pass's verdict, or null when it did not run (below
    /// <see cref="ModelEffort.Standard"/>) or failed. When present its gaps are carried into
    /// the brief as the run's stated objective, so the command pass is answering a specific
    /// screener's specific complaints rather than re-deriving them from the posting.
    /// </param>
    string Build(
        JobPosting posting,
        JobAnalysis analysis,
        CandidateSet candidates,
        ResumeDocument baseResume,
        TailorOptions options,
        AtsReview? atsReview = null);

    /// <summary>
    /// Estimates the token count of <paramref name="text"/> using a simple
    /// characters-divided-by-four heuristic. Approximate — not a real tokenizer.
    /// </summary>
    int EstimateTokens(string text);
}
