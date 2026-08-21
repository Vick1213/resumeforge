using ResumeForge.Domain.Formatting;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Formatting;

/// <summary>Tests for <see cref="BulletLineFit"/>.</summary>
public sealed class BulletLineFitTests
{
    /// <summary>A string of <paramref name="length"/> characters made of wrappable four-letter words.</summary>
    private static string Text(int length) =>
        string.Join(' ', Enumerable.Repeat("word", (length + 1) / 5)).PadRight(length, '!')[..length];

    [Theory]
    [InlineData(10, 1)]
    [InlineData(108, 1)]
    [InlineData(114, 2)]
    [InlineData(212, 2)]
    [InlineData(224, 3)]
    public void Line_count_follows_the_measured_column_width(int length, int expectedLines) =>
        BulletLineFit.LineCount(Text(length)).ShouldBe(expectedLines);

    [Fact]
    public void Text_fitting_one_line_is_never_ragged()
    {
        BulletLineFit.LastLineFill(Text(20)).ShouldBe(1d);
        BulletLineFit.IsRagged(Text(20)).ShouldBeFalse();
    }

    [Fact]
    public void A_wrapped_line_carrying_a_few_words_is_ragged() =>
        BulletLineFit.IsRagged(Text(110)).ShouldBeTrue();

    [Fact]
    public void A_wrapped_line_past_the_halfway_mark_is_not_ragged() =>
        BulletLineFit.IsRagged(Text(180)).ShouldBeFalse();

    [Fact]
    public void A_last_line_of_four_words_is_ragged_even_when_they_are_long_ones()
    {
        // Four 17-character words fill 71 of 108 columns — past MinLastLineFill, and still
        // exactly the "1-4 words alone on a line" shape the rule exists to prevent.
        var text = string.Join(' ', Enumerable.Repeat("internationalized", 10));

        BulletLineFit.LineCount(text).ShouldBe(2);
        BulletLineFit.LastLineFill(text).ShouldBeGreaterThan(BulletLineFit.MinLastLineFill);
        BulletLineFit.IsRagged(text).ShouldBeTrue();
    }

    [Fact]
    public void Target_length_for_one_line_ends_at_the_column_width() =>
        BulletLineFit.TargetLength(1).ShouldBe((1, BulletLineFit.CharsPerLine));

    [Fact]
    public void Target_length_for_two_lines_starts_halfway_through_the_second()
    {
        var (min, max) = BulletLineFit.TargetLength(2);

        min.ShouldBe(169);
        max.ShouldBe(210);
        BulletLineFit.IsRagged(Text(min)).ShouldBeFalse();

        // The maximum is the column's exact capacity, which only text that packs perfectly
        // reaches; a word boundary landing near it wraps a line early. The band is a target,
        // so what matters is that text just inside it is not ragged.
        BulletLineFit.IsRagged(Text(max - 4)).ShouldBeFalse();
    }

    [Fact]
    public void A_word_longer_than_the_column_takes_a_line_of_its_own() =>
        BulletLineFit.LineCount(new string('x', 200)).ShouldBe(1);
}
