using System.Text.RegularExpressions;

namespace ResumeForge.Application.Analysis;

/// <summary>
/// Classifies a <see cref="SeniorityLevel"/> purely from title text, by keyword cues
/// checked from most to least senior. Deliberately keyword-driven rather than a special
/// case for any single term, so the exact same rule can be applied to a job posting's
/// title (<see cref="JobAnalyzer"/>) and a resume experience entry's role
/// (<see cref="ResumeForge.Application.Scoring.Bm25RelevanceScorer"/>) — a title is
/// classified the same way on both sides of the match, which is what lets tailoring
/// compare a candidate's role level against a job's required level at all.
/// </summary>
public static partial class SeniorityClassifier
{
    /// <summary>
    /// Classifies <paramref name="title"/>, or <see cref="SeniorityLevel.Unknown"/> when
    /// it is null/blank or matches no cue.
    /// </summary>
    public static SeniorityLevel FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return SeniorityLevel.Unknown;
        }

        var lower = title.ToLowerInvariant();

        if (lower.Contains("principal", StringComparison.Ordinal))
        {
            return SeniorityLevel.Principal;
        }

        if (lower.Contains("staff", StringComparison.Ordinal))
        {
            return SeniorityLevel.Staff;
        }

        if (lower.Contains("senior", StringComparison.Ordinal) ||
            lower.Contains("sr.", StringComparison.Ordinal) ||
            lower.Contains("sr ", StringComparison.Ordinal) ||
            lower.Contains("lead", StringComparison.Ordinal))
        {
            return SeniorityLevel.Senior;
        }

        if (lower.Contains("intern", StringComparison.Ordinal))
        {
            return SeniorityLevel.Intern;
        }

        if (lower.Contains("junior", StringComparison.Ordinal) ||
            lower.Contains("jr.", StringComparison.Ordinal) ||
            lower.Contains("entry level", StringComparison.Ordinal) ||
            lower.Contains("entry-level", StringComparison.Ordinal))
        {
            return SeniorityLevel.Junior;
        }

        if (lower.Contains("mid-level", StringComparison.Ordinal) ||
            lower.Contains("mid level", StringComparison.Ordinal) ||
            MidLevelSuffixPattern().IsMatch(lower))
        {
            return SeniorityLevel.Mid;
        }

        return SeniorityLevel.Unknown;
    }

    [GeneratedRegex(@"\b(ii|iii|iv|2|3)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MidLevelSuffixPattern();
}
