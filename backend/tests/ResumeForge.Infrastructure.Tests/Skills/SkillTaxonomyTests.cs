using ResumeForge.Infrastructure.Skills;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Skills;

/// <summary>Tests for <see cref="SkillTaxonomy"/> against the real bundled <c>Data/skills.json</c>.</summary>
public sealed class SkillTaxonomyTests
{
    private readonly SkillTaxonomy _taxonomy = new();

    [Theory]
    [InlineData("k8s", "kubernetes")]
    [InlineData("K8s", "kubernetes")]
    [InlineData("c#", "csharp")]
    [InlineData("C#", "csharp")]
    [InlineData(".net", "dotnet")]
    [InlineData(".NET", "dotnet")]
    [InlineData("postgres", "postgresql")]
    [InlineData("Postgres", "postgresql")]
    [InlineData("kubernetes", "kubernetes")]
    [InlineData("csharp", "csharp")]
    public void TryCanonicalize_resolves_known_aliases(string raw, string expectedCanonical)
    {
        var resolved = _taxonomy.TryCanonicalize(raw, out var canonical);

        resolved.ShouldBeTrue();
        canonical.ShouldBe(expectedCanonical);
    }

    [Fact]
    public void TryCanonicalize_returns_false_for_an_unrecognized_skill()
    {
        var resolved = _taxonomy.TryCanonicalize("some-totally-made-up-skill-xyz", out var canonical);

        resolved.ShouldBeFalse();
        canonical.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("csharp", "languages")]
    [InlineData("dotnet", "frameworks")]
    [InlineData("postgresql", "datastores")]
    [InlineData("kubernetes", "cloud")]
    public void CategoryOf_returns_the_taxonomy_category(string canonical, string expectedCategory)
    {
        _taxonomy.CategoryOf(canonical).ShouldBe(expectedCategory);
    }

    [Fact]
    public void CategoryOf_returns_other_for_an_unrecognized_canonical_name()
    {
        _taxonomy.CategoryOf("not-a-real-canonical-name").ShouldBe("other");
    }

    [Fact]
    public void AllCanonical_contains_every_known_canonical_skill()
    {
        _taxonomy.AllCanonical.Count.ShouldBe(231);
        _taxonomy.AllCanonical.ShouldContain("kubernetes");
        _taxonomy.AllCanonical.ShouldContain("csharp");
    }

    [Fact]
    public void AliasesFor_returns_the_declared_aliases_for_a_canonical_skill()
    {
        var aliases = _taxonomy.AliasesFor("kubernetes");

        aliases.ShouldContain("k8s");
        aliases.ShouldContain("kubernetes");
    }

    [Fact]
    public void AliasesFor_returns_empty_for_an_unrecognized_canonical_name()
    {
        _taxonomy.AliasesFor("not-a-real-canonical-name").ShouldBeEmpty();
    }

    [Fact]
    public void TryCanonicalize_is_safe_to_call_concurrently()
    {
        var inputs = new[] { "k8s", "c#", ".net", "postgres", "react", "docker", "aws", "typescript" };

        Should.NotThrow(() =>
            Parallel.For(0, 2000, i => _taxonomy.TryCanonicalize(inputs[i % inputs.Length], out _)));
    }
}
