using ResumeForge.Domain.Formatting;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Formatting;

/// <summary>
/// Tests for <see cref="DateRangeFormatter"/>.
/// </summary>
public sealed class DateRangeFormatterTests
{
    [Fact]
    public void Format_with_end_date_uses_en_dash()
    {
        var text = DateRangeFormatter.Format(new DateOnly(2022, 3, 1), new DateOnly(2024, 11, 15));

        text.ShouldBe("Mar 2022 – Nov 2024");
    }

    [Fact]
    public void Format_with_null_end_date_shows_present()
    {
        var text = DateRangeFormatter.Format(new DateOnly(2022, 3, 1), null);

        text.ShouldBe("Mar 2022 – Present");
    }
}
