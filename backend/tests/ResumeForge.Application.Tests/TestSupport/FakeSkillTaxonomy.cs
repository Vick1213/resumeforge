using ResumeForge.Application.Abstractions;

namespace ResumeForge.Application.Tests.TestSupport;

/// <summary>A small, controllable <see cref="ISkillTaxonomy"/> test double.</summary>
public sealed class FakeSkillTaxonomy : ISkillTaxonomy
{
    private readonly Dictionary<string, string> _aliasToCanonical = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _canonicalToCategory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _canonicalToAliases = new(StringComparer.Ordinal);

    /// <summary>Registers a canonical skill, its category, and any alias spellings that resolve to it.</summary>
    public FakeSkillTaxonomy Add(string canonical, string category, params string[] aliases)
    {
        _canonicalToCategory[canonical] = category;
        _aliasToCanonical[canonical] = canonical;

        var list = _canonicalToAliases.TryGetValue(canonical, out var existing) ? existing : [];
        foreach (var alias in aliases)
        {
            _aliasToCanonical[alias] = canonical;
            list.Add(alias);
        }

        _canonicalToAliases[canonical] = list;
        return this;
    }

    /// <inheritdoc />
    public bool TryCanonicalize(string raw, out string canonical) => _aliasToCanonical.TryGetValue(raw, out canonical!);

    /// <inheritdoc />
    public IReadOnlySet<string> AllCanonical => new HashSet<string>(_canonicalToCategory.Keys, StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<string> AliasesFor(string canonical) =>
        _canonicalToAliases.TryGetValue(canonical, out var list) ? list : [];

    /// <inheritdoc />
    public string CategoryOf(string canonical) => _canonicalToCategory.TryGetValue(canonical, out var category) ? category : "other";

    /// <summary>A taxonomy pre-populated with a representative spread of skills across every category.</summary>
    public static FakeSkillTaxonomy CreateDefault() => new FakeSkillTaxonomy()
        .Add("csharp", "languages", "c#", "csharp")
        .Add("python", "languages", "python")
        .Add("typescript", "languages", "typescript", "ts")
        .Add("dotnet", "frameworks", ".net", "dotnet")
        .Add("react", "frameworks", "react", "reactjs")
        .Add("nodejs", "frameworks", "nodejs", "node")
        .Add("postgresql", "datastores", "postgresql", "postgres")
        .Add("redis", "datastores", "redis")
        .Add("kubernetes", "cloud", "kubernetes", "k8s")
        .Add("aws", "cloud", "aws")
        .Add("docker", "tools", "docker")
        .Add("git", "tools", "git")
        .Add("agile", "practices", "agile")
        .Add("cicd", "practices", "cicd", "ci/cd")
        .Add("leadership", "soft", "leadership")
        .Add("communication", "soft", "communication");
}
