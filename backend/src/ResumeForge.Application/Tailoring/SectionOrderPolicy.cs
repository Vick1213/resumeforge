using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic policy for where the education section sits, applied to every
/// <see cref="ResumeDocument.SectionOrder"/> the system produces.
/// </summary>
/// <remarks>
/// Recruiters and university career offices agree on the shape of the rule even where they
/// disagree on its exact cutoff: a candidate who is still studying — or who left school
/// recently enough that the degree is still their strongest single credential — leads with
/// education, and everyone else leads with work. What made this worth encoding rather than
/// leaving to the model is that the model kept getting it backwards: on an early-career
/// resume it proposed <c>setSectionOrder</c> with education last on run after run, because
/// "education goes at the bottom" is the overwhelmingly more common shape in its training
/// data. Placement is not a per-posting judgement call, so it is not the model's to make;
/// <see cref="Normalize(IReadOnlyList{SectionKind}, bool)"/> re-hoists education regardless of what the model asked for, and
/// leaves every other section exactly where the model put it.
/// </remarks>
public static class SectionOrderPolicy
{
    /// <summary>
    /// How long after graduation education keeps its position above the work history.
    /// Eighteen months covers the whole new-grad hiring cycle — the degree stays the headline
    /// credential through the first job and out the other side of it — without following
    /// someone into a career where their work has plainly become the stronger evidence.
    /// </summary>
    public const int EarlyCareerMonths = 18;

    /// <summary>
    /// Whether <paramref name="education"/> should lead: true when some included entry is
    /// still in progress (no end date) or ended within <see cref="EarlyCareerMonths"/> of
    /// <paramref name="now"/>, including a graduation date still in the future. A document
    /// with no included education entry never leads with one.
    /// </summary>
    public static bool EducationLeads(IEnumerable<EducationEntry> education, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(education);

        var cutoff = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-EarlyCareerMonths);

        foreach (var entry in education)
        {
            if (!entry.Included)
            {
                continue;
            }

            // A null end date is an in-progress degree, which always leads.
            if (entry.EndDate is not { } end || end >= cutoff)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <paramref name="order"/> with education moved to the front of the body — after
    /// the summary if one is present, first otherwise — when <paramref name="educationLeads"/>
    /// is set and it is not already ahead of both experience and projects. Every other
    /// section keeps its relative position. Returns the input unchanged when no move is
    /// needed, so callers can compare by reference to tell whether the policy did anything.
    /// </summary>
    public static IReadOnlyList<SectionKind> Normalize(IReadOnlyList<SectionKind> order, bool educationLeads)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!educationLeads)
        {
            return order;
        }

        var educationIndex = IndexOf(order, SectionKind.Education);
        if (educationIndex < 0)
        {
            return order;
        }

        var target = IndexOf(order, SectionKind.Summary) == 0 ? 1 : 0;
        if (educationIndex <= target)
        {
            return order;
        }

        var reordered = new List<SectionKind>(order);
        reordered.RemoveAt(educationIndex);
        reordered.Insert(target, SectionKind.Education);
        return reordered;
    }

    private static int IndexOf(IReadOnlyList<SectionKind> order, SectionKind section)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == section)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Convenience overload deriving the education-leads decision from
    /// <paramref name="education"/> itself.
    /// </summary>
    public static IReadOnlyList<SectionKind> Normalize(
        IReadOnlyList<SectionKind> order, IEnumerable<EducationEntry> education, DateTimeOffset now) =>
        Normalize(order, EducationLeads(education, now));
}
