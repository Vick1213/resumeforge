using System.Globalization;

namespace ResumeForge.Domain.Formatting;

/// <summary>
/// Formats date ranges for resume rendering, e.g. "Mar 2022 – Nov 2024" or
/// "Mar 2022 – Present".
/// </summary>
public static class DateRangeFormatter
{
    private const string EnDash = "–";

    /// <summary>
    /// Formats <paramref name="start"/> and <paramref name="end"/> as
    /// "MMM yyyy – MMM yyyy" using an en dash, or "MMM yyyy – Present" when
    /// <paramref name="end"/> is null. Always uses invariant culture.
    /// </summary>
    public static string Format(DateOnly start, DateOnly? end)
    {
        var startText = start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var endText = end is { } endDate
            ? endDate.ToString("MMM yyyy", CultureInfo.InvariantCulture)
            : "Present";

        return $"{startText} {EnDash} {endText}";
    }
}
