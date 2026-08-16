using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Text;

namespace ResumeForge.Infrastructure.Skills;

/// <summary>
/// <see cref="ISkillTaxonomy"/> backed by the embedded <c>Data/skills.json</c> resource
/// (231 skills, 698 aliases, across 7 categories). Loaded once from the assembly's
/// manifest resource stream and built into immutable, ordinal-ignore-case lookup tables,
/// so a single instance is safe to share as a singleton across concurrent requests.
/// </summary>
public sealed class SkillTaxonomy : ISkillTaxonomy
{
    private const string ResourceName = "ResumeForge.Infrastructure.Data.skills.json";
    private const string OtherCategory = "other";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly FrozenDictionary<string, string> _aliasToCanonical;
    private readonly FrozenDictionary<string, string> _canonicalToCategory;
    private readonly FrozenDictionary<string, IReadOnlyList<string>> _canonicalToAliases;
    private readonly FrozenDictionary<string, string> _canonicalToDisplay;
    private readonly FrozenSet<string> _allCanonical;

    /// <summary>Loads and parses the embedded taxonomy document immediately.</summary>
    public SkillTaxonomy()
    {
        var document = LoadDocument();

        var aliasToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalToAliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var canonicalToDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allCanonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in document.Categories)
        {
            foreach (var entry in category.Skills)
            {
                if (string.IsNullOrWhiteSpace(entry.Canonical))
                {
                    continue;
                }

                allCanonical.Add(entry.Canonical);
                canonicalToCategory[entry.Canonical] = category.Id;
                canonicalToAliases[entry.Canonical] = [.. entry.Aliases];
                canonicalToDisplay[entry.Canonical] = entry.Display is { Length: > 0 } display ? display : entry.Canonical;

                aliasToCanonical.TryAdd(SkillNormalizer.Normalize(entry.Canonical), entry.Canonical);

                foreach (var alias in entry.Aliases)
                {
                    var normalized = SkillNormalizer.Normalize(alias);
                    if (normalized.Length > 0)
                    {
                        aliasToCanonical.TryAdd(normalized, entry.Canonical);
                    }
                }
            }
        }

        _aliasToCanonical = aliasToCanonical.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _canonicalToCategory = canonicalToCategory.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _canonicalToAliases = canonicalToAliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _canonicalToDisplay = canonicalToDisplay.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _allCanonical = allCanonical.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> AllCanonical => _allCanonical;

    /// <inheritdoc />
    public bool TryCanonicalize(string raw, out string canonical)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var normalized = SkillNormalizer.Normalize(raw);
        if (normalized.Length > 0 && _aliasToCanonical.TryGetValue(normalized, out var match))
        {
            canonical = match;
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AliasesFor(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        return _canonicalToAliases.TryGetValue(canonical, out var aliases) ? aliases : [];
    }

    /// <inheritdoc />
    public string CategoryOf(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        return _canonicalToCategory.TryGetValue(canonical, out var category) ? category : OtherCategory;
    }

    /// <inheritdoc />
    public string DisplayNameOf(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        return _canonicalToDisplay.TryGetValue(canonical, out var display) ? display : canonical;
    }

    private static SkillsTaxonomyDocument LoadDocument()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found in {Assembly.GetExecutingAssembly().GetName().Name}. " +
                "This means Data/skills.json is missing its <EmbeddedResource> entry in the project file.");

        return JsonSerializer.Deserialize<SkillsTaxonomyDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' deserialized to null.");
    }
}
