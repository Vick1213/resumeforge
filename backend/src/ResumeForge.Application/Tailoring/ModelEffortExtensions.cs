namespace ResumeForge.Application.Tailoring;

/// <summary>Maps a <see cref="ModelEffort"/> to the preset defaults in CONTRACTS.md §6's table.</summary>
public static class ModelEffortExtensions
{
    /// <summary>
    /// The preset <see cref="TailorOptions.MaxRewrites"/> ceiling for <paramref name="effort"/>:
    /// 0 / 6 / 12 / 20 / 200 for Minimal / Standard / Thorough / Maximum / Full.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelEffort.Full"/>'s 200 is a backstop, not a budget: no resume has 200
    /// rewritable bullets, so the cap never binds and every bullet is in play — which is the
    /// point of the tier. It stays a finite number anyway so a malformed response cannot make
    /// the validator accept an unbounded command list.
    /// </remarks>
    public static int MaxRewrites(this ModelEffort effort) => effort switch
    {
        ModelEffort.Minimal => 0,
        ModelEffort.Standard => 6,
        ModelEffort.Thorough => 12,
        ModelEffort.Maximum => 20,
        ModelEffort.Full => 200,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "Unknown model effort."),
    };

    /// <summary>
    /// Resolves the effective <c>MaxRewrites</c> for a run: an explicit override always wins,
    /// since effort is a preset rather than a lock (CONTRACTS.md §6).
    /// </summary>
    public static int ResolveMaxRewrites(this ModelEffort effort, int? explicitOverride) =>
        explicitOverride ?? effort.MaxRewrites();

    /// <summary>
    /// Whether the run spends a second model call on the <c>ats-review</c> gap scan
    /// (CONTRACTS.md §6 "ATS review pass"). True from <see cref="ModelEffort.Standard"/> up.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelEffort.Minimal"/> is the one tier that skips it, and for a reason that
    /// is about usefulness rather than cost: Minimal cannot rewrite a bullet at all
    /// (<see cref="MaxRewrites"/> is 0 there), so a review whose whole output is "work these
    /// terms into these bullets" would be handing the command pass a list of things it is
    /// forbidden to do. Every tier that can act on the findings pays for them.
    /// </remarks>
    public static bool RunsAtsReview(this ModelEffort effort) => effort >= ModelEffort.Standard;
}
