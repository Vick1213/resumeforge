namespace ResumeForge.Application.Scoring;

/// <summary>
/// Tunable weights for <see cref="Bm25RelevanceScorer"/>. The three weights are
/// sum-normalized by <see cref="Normalize"/> before use, so any positive combination is
/// valid input and the effective weights always sum to 1.
/// </summary>
public sealed record ScoringOptions
{
    /// <summary>Weight of the BM25 textual-relevance term.</summary>
    public double Bm25Weight { get; init; } = 0.5;

    /// <summary>Weight of the canonical skill-overlap term.</summary>
    public double SkillWeight { get; init; } = 0.3;

    /// <summary>Weight of the recency term.</summary>
    public double RecencyWeight { get; init; } = 0.2;

    /// <summary>
    /// Returns the three weights divided by their sum, so they add to 1. Falls back to
    /// an equal three-way split when the configured weights sum to zero or less.
    /// </summary>
    public (double Bm25, double Skill, double Recency) Normalize()
    {
        var sum = Bm25Weight + SkillWeight + RecencyWeight;
        return sum > 0
            ? (Bm25Weight / sum, SkillWeight / sum, RecencyWeight / sum)
            : (1.0 / 3, 1.0 / 3, 1.0 / 3);
    }
}
