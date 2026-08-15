using System.Text;

namespace ResumeForge.Domain.Ids;

/// <summary>
/// Produces stable, lowercase slugs (matching <c>^[a-z0-9-]+$</c>) from arbitrary display
/// text, such as a knowledge-base filename or an entry title.
/// </summary>
public static class Slug
{
    private static readonly Dictionary<char, string> AsciiFoldMap = new()
    {
        ['à'] = "a", ['á'] = "a", ['â'] = "a", ['ã'] = "a", ['ä'] = "a", ['å'] = "a", ['ā'] = "a", ['ă'] = "a", ['ą'] = "a",
        ['æ'] = "ae",
        ['ç'] = "c", ['ć'] = "c", ['ĉ'] = "c", ['ċ'] = "c", ['č'] = "c",
        ['ď'] = "d", ['đ'] = "d", ['ð'] = "d",
        ['è'] = "e", ['é'] = "e", ['ê'] = "e", ['ë'] = "e", ['ē'] = "e", ['ĕ'] = "e", ['ė'] = "e", ['ę'] = "e", ['ě'] = "e",
        ['ĝ'] = "g", ['ğ'] = "g", ['ġ'] = "g", ['ģ'] = "g",
        ['ĥ'] = "h", ['ħ'] = "h",
        ['ì'] = "i", ['í'] = "i", ['î'] = "i", ['ï'] = "i", ['ĩ'] = "i", ['ī'] = "i", ['ĭ'] = "i", ['į'] = "i", ['ı'] = "i",
        ['ĵ'] = "j",
        ['ķ'] = "k",
        ['ĺ'] = "l", ['ļ'] = "l", ['ľ'] = "l", ['ŀ'] = "l", ['ł'] = "l",
        ['ñ'] = "n", ['ń'] = "n", ['ņ'] = "n", ['ň'] = "n", ['ŉ'] = "n",
        ['ò'] = "o", ['ó'] = "o", ['ô'] = "o", ['õ'] = "o", ['ö'] = "o", ['ø'] = "o", ['ō'] = "o", ['ŏ'] = "o", ['ő'] = "o",
        ['œ'] = "oe",
        ['ŕ'] = "r", ['ŗ'] = "r", ['ř'] = "r",
        ['ś'] = "s", ['ŝ'] = "s", ['ş'] = "s", ['š'] = "s", ['ß'] = "ss",
        ['ţ'] = "t", ['ť'] = "t", ['ŧ'] = "t",
        ['ù'] = "u", ['ú'] = "u", ['û'] = "u", ['ü'] = "u", ['ũ'] = "u", ['ū'] = "u", ['ŭ'] = "u", ['ů'] = "u", ['ű'] = "u", ['ų'] = "u",
        ['ŵ'] = "w",
        ['ý'] = "y", ['ÿ'] = "y", ['ŷ'] = "y",
        ['ź'] = "z", ['ż'] = "z", ['ž'] = "z",
    };

    /// <summary>
    /// Converts <paramref name="input"/> into a lowercase slug: accented Latin letters
    /// fold to their ASCII base letter, every run of characters that is neither a letter
    /// nor a digit collapses to a single '-', and leading/trailing '-' are trimmed. The
    /// result is deterministic and idempotent: <c>Slug.From(Slug.From(x)) == Slug.From(x)</c>.
    /// Throws <see cref="ArgumentException"/> when the input is null, empty, or folds to
    /// nothing (e.g. punctuation only).
    /// </summary>
    public static string From(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builder = new StringBuilder(input.Length);
        var pendingSeparator = false;

        foreach (var raw in input)
        {
            var lower = char.ToLowerInvariant(raw);

            if (AsciiFoldMap.TryGetValue(lower, out var folded))
            {
                AppendSeparatorIfPending(builder, ref pendingSeparator);
                builder.Append(folded);
                continue;
            }

            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                AppendSeparatorIfPending(builder, ref pendingSeparator);
                builder.Append(lower);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        var result = builder.ToString();
        if (result.Length == 0)
        {
            throw new ArgumentException("Input does not produce a valid slug.", nameof(input));
        }

        return result;
    }

    private static void AppendSeparatorIfPending(StringBuilder builder, ref bool pendingSeparator)
    {
        if (pendingSeparator && builder.Length > 0)
        {
            builder.Append('-');
        }

        pendingSeparator = false;
    }
}
