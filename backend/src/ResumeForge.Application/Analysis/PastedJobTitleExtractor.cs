using System.Text.RegularExpressions;

namespace ResumeForge.Application.Analysis;

/// <summary>
/// Deterministically extracts a job title from pasted job-description text
/// (CONTRACTS.md §9, <c>POST /api/jobs</c> pasted-<c>rawText</c> branch), where no
/// fetcher ever runs to populate <see cref="JobPosting.Title"/>. Deliberately
/// conservative: the extracted title becomes the tailored resume's headline, so a wrong
/// guess is worse than leaving it null. Only the first 10 non-blank lines are scanned,
/// and a line qualifies only if it is short, unambiguous, and either explicitly labeled
/// or reads like a role title rather than a sentence of prose.
/// </summary>
public static partial class PastedJobTitleExtractor
{
    private const int MaxLinesScanned = 10;
    private const int MaxTitleLength = 80;
    private const int MaxTitleWords = 12;

    /// <summary>
    /// Scans the first <see cref="MaxLinesScanned"/> non-blank lines of
    /// <paramref name="rawText"/>, in order, and returns the first one that qualifies as a
    /// job title — either an explicitly labeled line ("Title:", "Job Title:", "Position:",
    /// "Role:") or a short, non-sentence line containing a role keyword — or null when
    /// none qualifies.
    /// </summary>
    public static string? Extract(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var scanned = 0;

        foreach (var rawLine in SplitLines(rawText))
        {
            var normalized = CollapseWhitespace(rawLine.Trim());
            if (normalized.Length == 0)
            {
                continue;
            }

            scanned++;

            var labeled = LabeledTitlePattern().Match(normalized);
            if (labeled.Success)
            {
                var remainder = labeled.Groups[2].Value;
                if (IsWithinSanityBounds(remainder))
                {
                    return remainder;
                }
            }

            if (IsWithinSanityBounds(normalized) &&
                !normalized.EndsWith('.') &&
                RoleKeywordPattern().IsMatch(normalized))
            {
                return normalized;
            }

            if (scanned >= MaxLinesScanned)
            {
                break;
            }
        }

        return null;
    }

    private static bool IsWithinSanityBounds(string candidate) =>
        candidate.Length > 0 &&
        candidate.Length <= MaxTitleLength &&
        WordCount(candidate) <= MaxTitleWords;

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string CollapseWhitespace(string text) => WhitespacePattern().Replace(text, " ");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^(job title|title|position|role)\s*[:\-–]\s*(.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LabeledTitlePattern();

    [GeneratedRegex(RoleKeywordAlternation, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RoleKeywordPattern();

    /// <summary>
    /// Role-noun cues that make an unlabeled line look like a job title rather than a
    /// sentence of prose. Kept separate from <see cref="SeniorityClassifier"/>'s cue list,
    /// which classifies *seniority* (senior/staff/junior/intern/...) rather than
    /// identifying that a line names a *role* at all — the two lists answer different
    /// questions and only incidentally share a couple of words (e.g. "lead", "intern").
    /// </summary>
    private const string RoleKeywordAlternation =
        @"\b(engineer|engineering|developer|programmer|architect|manager|analyst|scientist|designer|consultant|administrator|specialist|technician|lead|intern|internship|devops|sre)\b";
}
