using ResumeForge.Domain.Ids;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Ids;

/// <summary>
/// Tests for <see cref="Slug"/> generation.
/// </summary>
public sealed class SlugTests
{
    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  Trim Me  ", "trim-me")]
    [InlineData("Über Café", "uber-cafe")]
    [InlineData("García & Muñoz", "garcia-munoz")]
    [InlineData("François", "francois")]
    [InlineData("Hello, World!!!", "hello-world")]
    [InlineData("---leading-and-trailing---", "leading-and-trailing")]
    [InlineData("multiple   spaces___here", "multiple-spaces-here")]
    [InlineData("Already-A-Slug", "already-a-slug")]
    [InlineData("C++ Developer", "c-developer")]
    public void From_produces_expected_slug(string input, string expected)
    {
        Slug.From(input).ShouldBe(expected);
    }

    [Fact]
    public void From_matches_slug_grammar()
    {
        var slug = Slug.From("Senior Software Engineer (Backend) #1");
        slug.ShouldMatch("^[a-z0-9-]+$");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("###!!!")]
    public void From_empty_or_unfoldable_input_throws(string input)
    {
        Should.Throw<ArgumentException>(() => Slug.From(input));
    }

    [Fact]
    public void From_null_throws()
    {
        Should.Throw<ArgumentNullException>(() => Slug.From(null!));
    }

    [Theory]
    [InlineData("Acme Corp")]
    [InlineData("Über Café!!")]
    [InlineData("already-a-slug")]
    [InlineData("Mixed_CASE---Input")]
    public void From_is_idempotent(string input)
    {
        var once = Slug.From(input);
        var twice = Slug.From(once);

        twice.ShouldBe(once);
    }
}
