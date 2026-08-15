using ResumeForge.Domain.Knowledge;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Knowledge;

/// <summary>
/// Tests for <see cref="KnowledgeDateValue"/> frontmatter date parsing.
/// </summary>
public sealed class KnowledgeDateValueTests
{
    [Fact]
    public void TryParse_full_date_format()
    {
        KnowledgeDateValue.TryParse("2022-03-15", out var value, out var isPresent).ShouldBeTrue();
        value.ShouldBe(new DateOnly(2022, 3, 15));
        isPresent.ShouldBeFalse();
    }

    [Fact]
    public void TryParse_year_month_format_maps_to_first_of_month()
    {
        KnowledgeDateValue.TryParse("2022-03", out var value, out var isPresent).ShouldBeTrue();
        value.ShouldBe(new DateOnly(2022, 3, 1));
        isPresent.ShouldBeFalse();
    }

    [Fact]
    public void TryParse_year_only_format_maps_to_january_first()
    {
        KnowledgeDateValue.TryParse("2022", out var value, out var isPresent).ShouldBeTrue();
        value.ShouldBe(new DateOnly(2022, 1, 1));
        isPresent.ShouldBeFalse();
    }

    [Theory]
    [InlineData("present")]
    [InlineData("Present")]
    [InlineData("PRESENT")]
    [InlineData("current")]
    [InlineData("Current")]
    public void TryParse_present_and_current_are_case_insensitive(string raw)
    {
        KnowledgeDateValue.TryParse(raw, out var value, out var isPresent).ShouldBeTrue();
        value.ShouldBeNull();
        isPresent.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_null_or_empty_means_no_date(string? raw)
    {
        KnowledgeDateValue.TryParse(raw, out var value, out var isPresent).ShouldBeTrue();
        value.ShouldBeNull();
        isPresent.ShouldBeFalse();
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2022/03/15")]
    [InlineData("March 2022")]
    [InlineData("22-03")]
    [InlineData("2022-13")]
    [InlineData("2022-13-01")]
    [InlineData("0000")]
    public void TryParse_garbage_returns_false(string raw)
    {
        KnowledgeDateValue.TryParse(raw, out var value, out var isPresent).ShouldBeFalse();
        value.ShouldBeNull();
        isPresent.ShouldBeFalse();
    }
}
