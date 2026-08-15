namespace ResumeForge.Application.Scoring;

/// <summary>A single bullet, skill, or entry scored for relevance against a job analysis.</summary>
public sealed record ScoredCandidate
{
    /// <summary>The candidate's own id — a bullet id or a skill id.</summary>
    public required string EntityId { get; init; }

    /// <summary>The candidate's display text.</summary>
    public required string Text { get; init; }

    /// <summary>Overall relevance, 0..1.</summary>
    public required double Score { get; init; }

    /// <summary>Ids of the requirements this candidate evidences.</summary>
    public required IReadOnlyList<string> MatchedRequirements { get; init; }
}

/// <summary>The full set of scored candidates offered to the tailoring model, by resume section.</summary>
public sealed record CandidateSet
{
    /// <summary>Scored experience bullets, most relevant first.</summary>
    public required IReadOnlyList<ScoredCandidate> Experience { get; init; }

    /// <summary>Scored project bullets, most relevant first.</summary>
    public required IReadOnlyList<ScoredCandidate> Projects { get; init; }

    /// <summary>Scored individual skills, most relevant first.</summary>
    public required IReadOnlyList<ScoredCandidate> Skills { get; init; }
}
