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
    /// Month abbreviations in the form resume convention uses, which is not the form
    /// <c>"MMM"</c> produces: June, July, and September are written <c>June</c>, <c>July</c>,
    /// and <c>Sept</c> rather than clipped to three letters, and none of them takes a
    /// trailing period. Spelling those three out also happens to be the safer choice for a
    /// parser — a resume parser matching months on a four-character prefix recognizes
    /// <c>June</c>, <c>July</c>, and <c>Sept</c> and misses <c>Jun</c>, <c>Jul</c>, and
    /// <c>Sep</c> — so the convention and the machine agree here.
    /// </summary>
    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "June", "July", "Aug", "Sept", "Oct", "Nov", "Dec"];

    /// <summary>The literal used in place of an end date for a role that has not ended.</summary>
    public const string PresentLabel = "Present";

    /// <summary>
    /// Formats <paramref name="start"/> and <paramref name="end"/> as
    /// "MMM yyyy – MMM yyyy" using an en dash, or "MMM yyyy – Present" when
    /// <paramref name="end"/> is null. Always uses invariant culture.
    /// </summary>
    public static string Format(DateOnly start, DateOnly? end)
    {
        var startText = FormatMonth(start);
        var endText = end is { } endDate ? FormatMonth(endDate) : PresentLabel;

        return $"{startText} {EnDash} {endText}";
    }

    /// <summary>Formats a single date as its month abbreviation and four-digit year.</summary>
    public static string FormatMonth(DateOnly date) =>
        string.Create(
            CultureInfo.InvariantCulture, $"{MonthNames[date.Month - 1]} {date.Year:D4}");
}
