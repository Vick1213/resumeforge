using ResumeForge.Domain.Text;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Text;

/// <summary>
/// Tests for <see cref="SkillNormalizer"/>.
/// </summary>
public sealed class SkillNormalizerTests
{
    [Theory]
    [InlineData("C++", "cpp")]
    [InlineData("C#", "csharp")]
    [InlineData(".NET", "dotnet")]
    [InlineData("Node.js", "nodejs")]
    [InlineData("JS", "javascript")]
    [InlineData("js", "javascript")]
    [InlineData("TS", "typescript")]
    [InlineData("K8s", "kubernetes")]
    [InlineData("Postgres", "postgresql")]
    [InlineData("PostgreSQL", "postgresql")]
    [InlineData("GH Actions", "githubactions")]
    [InlineData("GitHub Actions", "githubactions")]
    public void Normalize_uses_alias_table(string input, string expected)
    {
        SkillNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Python", "python")]
    [InlineData("  Go  ", "go")]
    [InlineData("React Native", "reactnative")]
    [InlineData("Ruby on Rails", "rubyonrails")]
    [InlineData("F#", "fsharp")]
    public void Normalize_falls_through_to_general_rule(string input, string expected)
    {
        SkillNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_is_deterministic()
    {
        SkillNormalizer.Normalize("Kubernetes").ShouldBe(SkillNormalizer.Normalize("Kubernetes"));
    }

    [Fact]
    public void Normalize_empty_returns_empty()
    {
        SkillNormalizer.Normalize("   ").ShouldBe(string.Empty);
    }
}
