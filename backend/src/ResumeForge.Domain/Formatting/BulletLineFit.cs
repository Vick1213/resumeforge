namespace ResumeForge.Domain.Formatting;

/// <summary>
/// Estimates how a bullet's text will break across rendered lines, so a bullet can be
/// written to fill the lines it occupies instead of spilling two words onto a line of its
/// own and leaving the rest of that line blank.
/// </summary>
/// <remarks>
/// <para>
/// A ragged last line is the single most visible way a resume looks unedited: the eye reads
/// the blank right-hand half as a mistake, and on a one-page document those fragments add up
/// to a whole entry's worth of space that carried no information. The fix is never
/// typographic — the text is set flush left in a fixed column, so nothing but the wording can
/// move the break — which is why this lives here, as a constraint on what may be written,
/// rather than in the renderer.
/// </para>
/// <para>
/// Estimation is by character count against <see cref="CharsPerLine"/>, which is exact only
/// for text of average width: the resume is set in a proportional face, so a bullet full of
/// capitals and digits runs wider than the same character count of lowercase prose. It is
/// deliberately calibrated against the *narrowest* line the renderer produces (see
/// <see cref="CharsPerLine"/>), so the error runs in the safe direction — a bullet this
/// helper calls a full line may fall a little short of one, but one it calls full will not
/// have silently wrapped.
/// </para>
/// </remarks>
public static class BulletLineFit
{
    /// <summary>
    /// Characters that fit on one bullet line, measured from real rendered output rather than
    /// derived from font metrics.
    /// </summary>
    /// <remarks>
    /// Measured by rendering bullets of known length through the PDF renderer and reading the
    /// wrap points back out of the produced PDF's text layer. At 10pt with half-inch margins
    /// and the renderer's fixed 1.0 scale (the fit search no longer zooms), a realistic
    /// mixed-case bullet — capitals, digits, punctuation, the widest text a resume carries —
    /// broke between 110 and 113 characters; lowercase prose between 112 and 117. One
    /// hundred eight is taken: the bottom of the wide-case range, minus two, because the
    /// error has to fall on the side of a bullet that fits.
    /// </remarks>
    public const int CharsPerLine = 108;

    /// <summary>
    /// How full a wrapped bullet's last line must be, as a fraction of
    /// <see cref="CharsPerLine"/>, before it stops reading as a ragged fragment. A bullet
    /// occupying a single line is never ragged, however short — a short one-liner reads as
    /// deliberate; a long bullet trailing three words onto a second line does not.
    /// </summary>
    /// <remarks>
    /// A tuned threshold, not a quoted one: no source states a fraction. It is the strictest
    /// reading of the three that come close. r/EngineeringResumes bans the outcome rather
    /// than measuring it — "Don't spill bullets onto the following line with only 1-4 words
    /// on it. It's an extreme waste of space" — which at roughly six characters a word puts
    /// the floor near a quarter of a line. A hiring manager writing for CareerAddict says to
    /// "aim to fill at least half of the line". Print typography's standard fix forbids a
    /// last line under about twenty characters. Half plus a margin for the fact that this
    /// estimate is made in characters and the page is set in a proportional face lands at
    /// 0.55.
    /// </remarks>
    public const double MinLastLineFill = 0.55;

    /// <summary>
    /// The absolute floor on a wrapped bullet's last line, independent of
    /// <see cref="MinLastLineFill"/>: the rule being enforced is "never 1-4 words alone on a
    /// line", and a line of one very long word can clear a fill fraction while still being
    /// the thing the rule exists to prevent.
    /// </summary>
    public const int MinLastLineWords = 5;

    /// <summary>
    /// Columns left unused at a line break by the word boundary the break falls on. Held back
    /// from the top of every multi-line band in <see cref="TargetLength"/> so a bullet written
    /// to the top of its band still occupies the number of lines the band is named for. Seven
    /// is roughly the average length of an English word plus its space — the worst case for a
    /// greedy wrap is one whole word left behind.
    /// </summary>
    public const int WordBreakAllowance = 7;

    /// <summary>The number of lines <paramref name="text"/> is estimated to occupy.</summary>
    public static int LineCount(string text, int charsPerLine = CharsPerLine) =>
        Wrap(text, charsPerLine).Count;

    /// <summary>
    /// How full the last line of <paramref name="text"/> is, from 0 to 1. Text that fits on a
    /// single line reports <c>1</c>, since it has no line it failed to fill.
    /// </summary>
    public static double LastLineFill(string text, int charsPerLine = CharsPerLine)
    {
        var lines = Wrap(text, charsPerLine);
        return lines.Count <= 1 ? 1d : Math.Min(1d, (double)lines[^1] / charsPerLine);
    }

    /// <summary>
    /// Whether <paramref name="text"/> wraps and then leaves its last line less than
    /// <see cref="MinLastLineFill"/> full, or carrying fewer than
    /// <see cref="MinLastLineWords"/> words.
    /// </summary>
    public static bool IsRagged(string text, int charsPerLine = CharsPerLine)
    {
        var lines = Wrap(text, charsPerLine);
        if (lines.Count <= 1)
        {
            return false;
        }

        return (double)lines[^1] / charsPerLine < MinLastLineFill || LastLineWordCount(text, charsPerLine) < MinLastLineWords;
    }

    /// <summary>The number of words on <paramref name="text"/>'s last rendered line.</summary>
    private static int LastLineWordCount(string text, int charsPerLine)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = Wrap(text, charsPerLine);

        if (lines.Count <= 1)
        {
            return words.Length;
        }

        var count = 0;
        var used = 0;

        // Re-walk the wrap to find where the last line starts, counting from the end.
        for (var i = words.Length - 1; i >= 0; i--)
        {
            var addition = count == 0 ? words[i].Length : words[i].Length + 1;
            if (used + addition > lines[^1])
            {
                break;
            }

            used += addition;
            count++;
        }

        return count;
    }

    /// <summary>
    /// The character range a bullet should land in to fill <paramref name="lines"/> lines
    /// without spilling into the next: from <see cref="MinLastLineFill"/> of the way through
    /// the last line up to its end. Used to state the target as a concrete number of
    /// characters where a writer — or a model — can act on it.
    /// </summary>
    public static (int Min, int Max) TargetLength(int lines, int charsPerLine = CharsPerLine)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lines, 1);

        // Each line break eats the space that would have separated the two words it falls
        // between, so a bullet occupying n full lines is n*charsPerLine + (n-1) characters
        // long, not n*charsPerLine. Ignoring that put the bottom of the band one character
        // inside the ragged range it was meant to exclude.
        //
        // The ceiling then comes back down by WordBreakAllowance per break, because no real
        // sentence packs a line to its last column: the break lands at whatever word boundary
        // comes before it, leaving a few columns unused. Text written to the arithmetic
        // maximum therefore wraps one line further than the arithmetic says, which is the
        // exact failure this band exists to prevent.
        var max = (lines * charsPerLine) + (lines - 1) - ((lines - 1) * WordBreakAllowance);
        var min = lines == 1
            ? 1
            : ((lines - 1) * (charsPerLine + 1)) + (int)Math.Ceiling(MinLastLineFill * charsPerLine);

        return (min, max);
    }

    /// <summary>
    /// Greedy word wrap, returning the character length of each resulting line. A single word
    /// longer than <paramref name="charsPerLine"/> occupies its own line rather than being
    /// split, which matches how the renderer treats an unbreakable token such as a URL.
    /// </summary>
    private static List<int> Wrap(string text, int charsPerLine)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(charsPerLine, 1);

        var lines = new List<int>();
        var current = 0;

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (current == 0)
            {
                current = word.Length;
                continue;
            }

            if (current + 1 + word.Length <= charsPerLine)
            {
                current += 1 + word.Length;
            }
            else
            {
                lines.Add(current);
                current = word.Length;
            }
        }

        if (current > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }

        return lines;
    }
}
