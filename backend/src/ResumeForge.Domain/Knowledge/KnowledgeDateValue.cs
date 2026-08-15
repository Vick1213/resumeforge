using System.Globalization;

namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// Parses the date values that appear in knowledge-base frontmatter (<c>startDate</c>,
/// <c>endDate</c>): <c>yyyy-MM-dd</c>, <c>yyyy-MM</c>, <c>yyyy</c>, or the literal
/// <c>present</c>/<c>current</c> meaning "ongoing, no end date".
/// </summary>
public static class KnowledgeDateValue
{
    /// <summary>
    /// Attempts to parse <paramref name="raw"/>. A null, empty, or whitespace-only input
    /// is treated as "no date given" and succeeds with a null <paramref name="value"/>
    /// and <paramref name="isPresent"/> false. The literal <c>present</c> or
    /// <c>current</c> (case-insensitive) succeeds with a null <paramref name="value"/>
    /// and <paramref name="isPresent"/> true. <c>yyyy</c> resolves to January 1st of that
    /// year; <c>yyyy-MM</c> resolves to the 1st of that month. Returns false for anything
    /// else.
    /// </summary>
    public static bool TryParse(string? raw, out DateOnly? value, out bool isPresent)
    {
        value = null;
        isPresent = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();

        if (string.Equals(text, "present", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "current", StringComparison.OrdinalIgnoreCase))
        {
            isPresent = true;
            return true;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            value = exact;
            return true;
        }

        if (text.Length == 7 && text[4] == '-'
            && TryParseDigits(text.AsSpan(0, 4), out var yearForMonth)
            && TryParseDigits(text.AsSpan(5, 2), out var month)
            && month is >= 1 and <= 12
            && yearForMonth is >= 1 and <= 9999)
        {
            value = new DateOnly(yearForMonth, month, 1);
            return true;
        }

        if (text.Length == 4 && TryParseDigits(text.AsSpan(), out var year) && year is >= 1 and <= 9999)
        {
            value = new DateOnly(year, 1, 1);
            return true;
        }

        return false;
    }

    private static bool TryParseDigits(ReadOnlySpan<char> span, out int value) =>
        int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
