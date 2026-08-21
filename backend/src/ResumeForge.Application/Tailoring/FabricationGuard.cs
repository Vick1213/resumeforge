namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="IFabricationGuard"/>. Extracts two kinds of "evidence" tokens
/// from bullet text: number-like tokens (anything containing a digit — plain numbers,
/// percentages, currency, multipliers like <c>"3x"</c>, and identifiers like <c>"p99"</c>)
/// and proper-noun-like tokens (capitalized words with a following lowercase letter).
/// Tokens are compared after stripping surrounding punctuation, commas, currency symbols,
/// and casing, so equivalent formattings never register as a change. A rewrite may neither
/// introduce a number the original did not have nor drop every number the original did —
/// the guard runs in both directions, because a resume loses as much to a quietly deleted
/// metric as it does to an invented one.
/// </summary>
public sealed class FabricationGuard : IFabricationGuard
{
    /// <inheritdoc />
    public bool IsSafe(string originalText, string rewrittenText, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(rewrittenText);

        var (originalNumbers, originalProperNouns) = ExtractTokens(originalText);
        var (rewrittenNumbers, rewrittenProperNouns) = ExtractTokens(rewrittenText);

        var newNumbers = rewrittenNumbers
            .Where(n => !originalNumbers.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (newNumbers.Count > 0)
        {
            reason = $"Rewrite introduces number(s) not present in the original: {string.Join(", ", newNumbers)}.";
            return false;
        }

        // A quantified bullet is the whole reason the bullet is on the page: "cut spend by
        // ~$2K/month" and "redesigned the deployment pipeline" describe the same work, and
        // only one of them is evidence. Losing the number is therefore treated as its own
        // failure rather than being excused by a surviving proper noun — a rewrite that keeps
        // "AWS" while dropping "$2K/month" trades the claim for the tech stack.
        if (originalNumbers.Count > 0 && !rewrittenNumbers.Overlaps(originalNumbers))
        {
            var dropped = originalNumbers.OrderBy(n => n, StringComparer.Ordinal);
            reason = $"Rewrite drops every quantified result from the original: {string.Join(", ", dropped)}.";
            return false;
        }

        if (originalProperNouns.Count > 0 &&
            originalNumbers.Count == 0 &&
            !rewrittenProperNouns.Overlaps(originalProperNouns))
        {
            reason = "Rewrite drops every number and proper noun present in the original bullet.";
            return false;
        }

        reason = null;
        return true;
    }

    private static (HashSet<string> Numbers, HashSet<string> ProperNouns) ExtractTokens(string text)
    {
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var properNouns = new HashSet<string>(StringComparer.Ordinal);
        var wordIndex = 0;

        foreach (var rawWord in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = rawWord.Trim('(', ')', '"', '\'', ',', ';', ':', '!', '?', '[', ']', '{', '}');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var isFirstWord = wordIndex == 0;
            wordIndex++;

            if (trimmed.Any(char.IsAsciiDigit))
            {
                numbers.Add(NormalizeNumberToken(trimmed));
                continue;
            }

            // Skip the sentence-initial word for proper-noun purposes: every bullet starts
            // capitalized regardless of whether its first word is actually a proper noun,
            // so treating it as evidence would falsely reject rewrites that merely open
            // with a different verb.
            if (!isFirstWord && char.IsAsciiLetterUpper(trimmed[0]) && trimmed.Skip(1).Any(char.IsAsciiLetterLower))
            {
                properNouns.Add(trimmed.TrimEnd('.', ',', ';', ':', '!', '?').ToLowerInvariant());
            }
        }

        return (numbers, properNouns);
    }

    private static string NormalizeNumberToken(string token)
    {
        var cleaned = token
            .TrimEnd('.', ',', ';', ':', ')', '!', '?')
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal);

        return cleaned.ToLowerInvariant();
    }
}
