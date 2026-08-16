using System.Text.RegularExpressions;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// The scoring algorithm <see cref="HeuristicLanguageModel"/> uses to resolve autofill
/// fields to canonical keys with no network call — a server-side mirror of the extension's
/// tier-2 heuristic matcher (<c>extension/src/heuristics/matcher.ts</c>), so a field that
/// escalates to the model fallback is judged by the same token-overlap rules, just with a
/// larger canonical-key/synonym search and without the client's accept-threshold gate
/// (every fill here is still previewed in the extension's overlay before it's applied, per
/// CONTRACTS.md §10).
/// </summary>
internal static class AutofillFieldMatcher
{
    /// <summary>
    /// Below this, a score is treated as noise and clamped to 0 — "genuinely unmappable" —
    /// rather than reported as a weak positive. Mirrors the extension's
    /// <c>MIN_CONFIDENCE_FLOOR</c>.
    /// </summary>
    public const double MinConfidenceFloor = 0.34;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "were", "you", "your", "yours", "to", "of", "for",
        "in", "on", "at", "this", "that", "i", "we", "will", "do", "does", "did", "please",
        "enter", "select", "choose", "provide", "and", "or", "be", "if", "my", "our",
    };

    private static readonly Regex CamelBoundary = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumeric = new("[^a-zA-Z0-9]+", RegexOptions.Compiled);

    /// <summary>Splits camelCase/snake_case/kebab-case/punctuated text into lowercase word tokens.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var spaced = CamelBoundary.Replace(text, "$1 $2");
        spaced = NonAlphaNumeric.Replace(spaced, " ").ToLowerInvariant();

        return spaced
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Stopwords.Contains(t))
            .ToList();
    }

    /// <summary>
    /// Scores one field's label/name/placeholder against one canonical key. An exact
    /// <c>autocomplete</c> token match short-circuits to 1.0; otherwise the best
    /// full-phrase-containment score across every source and every synonym phrase for
    /// <paramref name="canonicalKey"/> wins, clamped to 0 below <see cref="MinConfidenceFloor"/>.
    /// </summary>
    public static double ScoreField(string? label, string? name, string? placeholder, string? autoComplete, string canonicalKey)
    {
        if (!string.IsNullOrWhiteSpace(autoComplete))
        {
            var token = NormalizedAutocompleteToken(autoComplete);
            if (token.Length > 0 &&
                AutofillFieldSynonyms.AutocompleteMap.TryGetValue(token, out var mapped) &&
                string.Equals(mapped, canonicalKey, StringComparison.Ordinal))
            {
                return 1.0;
            }
        }

        if (!AutofillFieldSynonyms.Synonyms.TryGetValue(canonicalKey, out var phrases))
        {
            return 0;
        }

        double best = 0;
        foreach (var source in new[] { label, name, placeholder })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var sourceTokens = Tokenize(source);
            foreach (var phrase in phrases)
            {
                var score = FullPhraseMatchScore(sourceTokens, phrase);
                if (score > best)
                {
                    best = score;
                }
            }
        }

        return best < MinConfidenceFloor ? 0 : best;
    }

    /// <summary>
    /// Resolves the single best-scoring canonical key for a field out of
    /// <paramref name="canonicalKeys"/>. Returns an empty key and confidence 0 when nothing
    /// clears the floor — "genuinely unmappable" — rather than guessing.
    /// </summary>
    public static (string CanonicalKey, double Confidence) ResolveBestKey(
        IReadOnlyList<string> canonicalKeys, string? label, string? name, string? placeholder, string? autoComplete)
    {
        var bestKey = string.Empty;
        var bestScore = 0.0;

        foreach (var key in canonicalKeys)
        {
            var score = ScoreField(label, name, placeholder, autoComplete, key);
            if (score > bestScore)
            {
                bestScore = score;
                bestKey = key;
            }
        }

        return bestScore > 0 ? (bestKey, bestScore) : (string.Empty, 0.0);
    }

    /// <summary>
    /// Fuzzy-matches <paramref name="target"/> against a list of option texts: an exact
    /// case-insensitive match, then substring containment either direction, then token
    /// overlap — returning <c>null</c> (never a guess) when nothing clears a reasonable
    /// overlap bar. Mirrors the extension's <c>fuzzyMatchOption</c>
    /// (<c>extension/src/content/fill.ts</c>).
    /// </summary>
    public static string? FuzzyMatchOption(IReadOnlyList<string> options, string target)
    {
        var normalizedTarget = target.Trim().ToLowerInvariant();
        if (normalizedTarget.Length == 0)
        {
            return null;
        }

        foreach (var option in options)
        {
            if (string.Equals(option.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        foreach (var option in options)
        {
            var norm = option.Trim().ToLowerInvariant();
            if (norm.Length > 0 && (norm.Contains(normalizedTarget, StringComparison.Ordinal) || normalizedTarget.Contains(norm, StringComparison.Ordinal)))
            {
                return option;
            }
        }

        var targetTokens = new HashSet<string>(Tokenize(normalizedTarget), StringComparer.Ordinal);
        if (targetTokens.Count == 0)
        {
            return null;
        }

        const double MinOptionOverlap = 0.5;
        string? best = null;
        var bestScore = 0.0;

        foreach (var option in options)
        {
            var optionTokens = new HashSet<string>(Tokenize(option), StringComparer.Ordinal);
            if (optionTokens.Count == 0)
            {
                continue;
            }

            var intersectionSize = targetTokens.Count(t => optionTokens.Contains(t));
            var union = new HashSet<string>(targetTokens, StringComparer.Ordinal);
            union.UnionWith(optionTokens);
            var score = union.Count == 0 ? 0 : (double)intersectionSize / union.Count;

            if (score > bestScore)
            {
                bestScore = score;
                best = option;
            }
        }

        return best is not null && bestScore >= MinOptionOverlap ? best : null;
    }

    private static double FullPhraseMatchScore(IReadOnlyList<string> sourceTokens, string phrase)
    {
        if (sourceTokens.Count == 0)
        {
            return 0;
        }

        var phraseTokens = Tokenize(phrase);
        if (phraseTokens.Count == 0)
        {
            return 0;
        }

        var sourceSet = new HashSet<string>(sourceTokens, StringComparer.Ordinal);
        foreach (var t in phraseTokens)
        {
            if (!sourceSet.Contains(t))
            {
                return 0;
            }
        }

        // Reward phrases that explain most of the source text; a phrase fully contained in
        // a short, closely-matching source scores near 1.0, while the same phrase buried in
        // a much longer, noisier source scores lower.
        return (double)phraseTokens.Count / sourceTokens.Count;
    }

    private static string NormalizedAutocompleteToken(string autoComplete)
    {
        var parts = autoComplete.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : string.Empty;
    }
}
