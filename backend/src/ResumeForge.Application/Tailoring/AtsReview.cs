namespace ResumeForge.Application.Tailoring;

/// <summary>
/// The verdict of the <c>ats-review</c> pass: a first model call that reads the posting and
/// the <em>untailored</em> base resume as a screener would, and reports what is missing
/// before a second call is asked to fix it (CONTRACTS.md §6 "ATS review pass").
/// </summary>
/// <remarks>
/// Two audiences have to be cleared, and they fail a resume for different reasons.
///
/// The <em>ATS</em> matches the posting's vocabulary against the document's, so a term the
/// posting leans on and the resume never says costs the candidate the screen no matter how
/// obviously they can do the work. That is a keyword problem, and a keyword problem can be
/// solved anywhere on the page.
///
/// The <em>recruiter</em> reads for evidence. A keyword parked in the skills list satisfies
/// the parser and tells a human nothing: it carries no scale, no outcome, and no story about
/// where the candidate actually used it. Clearing the second audience means the same term
/// appears inside a bullet, attached to what was built and what it moved.
///
/// So <see cref="AtsGap"/> does not just name the missing term — it says where it belongs and
/// what claim it should be evidencing, which is exactly the input the command-proposing pass
/// needs to write a bullet instead of padding a list.
/// </remarks>
public sealed record AtsReview
{
    /// <summary>
    /// How the base resume scores against this posting today, 0-100, as the screener reads
    /// it. Reported before anything is changed, so the pair of scores brackets the run.
    /// </summary>
    public required int ScoreBefore { get; init; }

    /// <summary>
    /// What the same screener would score the resume at if every gap in
    /// <see cref="Gaps"/> were addressed — the ceiling this run is working toward, not a
    /// promise about what it reached.
    /// </summary>
    public required int ScoreAfter { get; init; }

    /// <summary>One or two sentences on why the resume scores where it does.</summary>
    public required string Verdict { get; init; }

    /// <summary>What the posting asks for that the resume does not currently evidence.</summary>
    public required IReadOnlyList<AtsGap> Gaps { get; init; }

    /// <summary>
    /// What a human reviewer would hold against the resume beyond keyword matching — a
    /// claim with no outcome attached, a buried headline achievement, an unexplained gap.
    /// Free text, one entry per observation.
    /// </summary>
    public required IReadOnlyList<string> RecruiterNotes { get; init; }
}

/// <summary>A single thing the posting asks for that the base resume does not evidence.</summary>
public sealed record AtsGap
{
    /// <summary>The posting's own term for it, spelled as the posting spells it.</summary>
    public required string Keyword { get; init; }

    /// <summary>How much the posting leans on it.</summary>
    public required AtsGapImportance Importance { get; init; }

    /// <summary>
    /// True when the resume already lists the term in its skills section but no bullet
    /// anywhere puts it in context. This is the gap that passes the parser and fails the
    /// human, and it is the one the command pass is expected to fix by rewriting a bullet
    /// rather than by touching the skills list.
    /// </summary>
    public required bool SkillsOnly { get; init; }

    /// <summary>
    /// Where it belongs: an entry or bullet id from the resume the review was given
    /// (<c>exp:…</c>, <c>prj:…</c>), or a plain section name when no single bullet is the
    /// natural home. Advisory — the command pass targets whatever it judges best.
    /// </summary>
    public string? Placement { get; init; }

    /// <summary>
    /// The claim this keyword should be evidencing, in the candidate's own existing
    /// material: what they did with it and what it moved. This is what turns a keyword into
    /// a bullet a recruiter believes, and it is the reason the review is worth a model call
    /// that a keyword-frequency diff would not be.
    /// </summary>
    public required string Angle { get; init; }
}

/// <summary>How hard a posting leans on a term the resume is missing.</summary>
public enum AtsGapImportance
{
    /// <summary>Named as a requirement; a screen is unlikely to be survivable without it.</summary>
    Critical,

    /// <summary>Named more than in passing, and its absence is noticeable.</summary>
    Important,

    /// <summary>Mentioned, and worth having if it fits without cost.</summary>
    NiceToHave,
}
