using System.Text.Json;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Ids;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// No-network <see cref="ILanguageModel"/> used whenever no API key is configured for any
/// provider, so the product works end to end without one. Supports both schemas the port
/// ever names:
///
/// <list type="bullet">
/// <item>
/// <c>"tailor-commands"</c> — parses the compact brief produced by <c>BriefBuilder</c> and
/// proposes commands by pure ranking rules: the brief's candidate order already reflects
/// relevance (CONTRACTS.md §6, §7), so the heuristic keeps the top-scored entries within
/// <see cref="TailorOptions"/>'s caps, excludes the rest, orders each kept entry's bullets
/// by that same relevance order, selects an existing variant over generating prose whenever
/// one is available, and emphasizes the skills the job description's candidate set
/// surfaced. It never emits a <see cref="RewriteCommand"/> and its output is deterministic
/// for a given brief.
/// </item>
/// <item>
/// <c>"field-resolutions"</c> — parses the brief produced by
/// <c>AutofillEndpoints.BuildBrief</c> and resolves each unresolved field to a canonical key
/// by the same token-overlap scoring the extension's tier-2 heuristic matcher uses (see
/// <see cref="AutofillFieldMatcher"/>), so <c>POST /api/autofill/resolve</c> works with no
/// key configured (CONTRACTS.md §10). For a select/radio field it also looks up the
/// candidate's own knowledge-base value for the resolved key and fuzzy-matches it against
/// the field's options to propose an <c>optionValue</c>.
/// </item>
/// </list>
/// </summary>
public sealed class HeuristicLanguageModel(TailorOptions tailorOptions, IKnowledgeBaseReader knowledgeBaseReader) : ILanguageModel
{
    private const string RequirementsHeader = "REQUIREMENTS";
    private const string ExperienceHeader = "CANDIDATES-EXPERIENCE";
    private const string ProjectsHeader = "CANDIDATES-PROJECTS";
    private const string SkillsHeader = "CANDIDATES-SKILLS";

    private const string CanonicalKeysPrefix = "CANONICAL-KEYS:";
    private const string HostPrefix = "HOST:";
    private const string FieldsHeader = "FIELDS";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public string ModelId => "heuristic-v1";

    /// <inheritdoc />
    public async Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        object result = request.SchemaName switch
        {
            JsonSchemaRegistry.TailorCommandsSchemaName => BuildCommands(request.User),
            JsonSchemaRegistry.FieldResolutionsSchemaName => await ResolveFieldsAsync(request.User, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"{nameof(HeuristicLanguageModel)} does not support schema '{request.SchemaName}'; it only proposes " +
                $"'{JsonSchemaRegistry.TailorCommandsSchemaName}' and '{JsonSchemaRegistry.FieldResolutionsSchemaName}'."),
        };

        // Round-trip through JSON rather than casting directly, so this works for any T the
        // caller requests for this schema (a list, an array, etc.) the same way a real
        // model response would be deserialized.
        var json = JsonSerializer.Serialize(result, result.GetType(), SerializerOptions);
        var value = JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Heuristic completion produced a null value.");

        return new ModelResponse<T>
        {
            Value = value,
            Usage = TokenUsage.Empty,
            FromCache = false,
        };
    }

    private List<TailorCommand> BuildCommands(string brief)
    {
        var (experience, projects, skillIds) = ParseBrief(brief);

        var commands = new List<TailorCommand>();
        AppendEntryCommands(commands, experience, tailorOptions.MaxExperienceEntries);
        AppendEntryCommands(commands, projects, tailorOptions.MaxProjectEntries);
        AppendEmphasizeSkills(commands, skillIds);

        return commands;
    }

    private static (List<(string Id, int VariantCount)> Experience, List<(string Id, int VariantCount)> Projects, List<string> SkillIds) ParseBrief(string brief)
    {
        var experience = new List<(string, int)>();
        var projects = new List<(string, int)>();
        var skills = new List<string>();
        var section = string.Empty;

        foreach (var rawLine in brief.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line is RequirementsHeader or ExperienceHeader or ProjectsHeader or SkillsHeader)
            {
                section = line;
                continue;
            }

            if (section == ExperienceHeader || section == ProjectsHeader)
            {
                var parts = line.Split('|', 3);
                if (parts.Length < 2)
                {
                    continue;
                }

                var variantCount = 0;
                if (parts[1].Length > 1 && parts[1][0] == 'v')
                {
                    _ = int.TryParse(parts[1].AsSpan(1), out variantCount);
                }

                (section == ExperienceHeader ? experience : projects).Add((parts[0], variantCount));
            }
            else if (section == SkillsHeader)
            {
                var parts = line.Split('|', 2);
                if (parts.Length >= 1 && parts[0].Length > 0)
                {
                    skills.Add(parts[0]);
                }
            }
        }

        return (experience, projects, skills);
    }

    private static void AppendEntryCommands(List<TailorCommand> commands, List<(string Id, int VariantCount)> candidates, int maxEntries)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var entryOrder = new List<string>();
        var seenEntries = new HashSet<string>(StringComparer.Ordinal);
        var bulletsByEntry = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seenBulletsByEntry = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var variantCountByBullet = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (bulletId, variantCount) in candidates)
        {
            if (!EntityId.TryParse(bulletId, out var id))
            {
                continue;
            }

            var entryId = id.Parent.ToString();
            if (seenEntries.Add(entryId))
            {
                entryOrder.Add(entryId);
                bulletsByEntry[entryId] = [];
                seenBulletsByEntry[entryId] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (seenBulletsByEntry[entryId].Add(bulletId))
            {
                bulletsByEntry[entryId].Add(bulletId);
                variantCountByBullet[bulletId] = variantCount;
            }
        }

        var keptCount = Math.Min(entryOrder.Count, Math.Max(0, maxEntries));
        var kept = entryOrder.Take(keptCount).ToList();
        var excluded = entryOrder.Skip(keptCount).ToList();

        if (kept.Count > 0)
        {
            commands.Add(new IncludeCommand { Targets = kept, Rationale = "Best-scoring entries for this job description." });

            if (kept.Count > 1)
            {
                commands.Add(new OrderCommand { Parent = "root", Order = kept, Rationale = "Ordered by relevance to the job description." });
            }
        }

        if (excluded.Count > 0)
        {
            commands.Add(new ExcludeCommand { Targets = excluded, Rationale = "Lower-scoring entries, trimmed to fit the target length." });
        }

        foreach (var entryId in kept)
        {
            var bulletIds = bulletsByEntry[entryId];

            if (bulletIds.Count > 1)
            {
                commands.Add(new OrderCommand { Parent = entryId, Order = bulletIds, Rationale = "Bullets ordered by relevance to the job description." });
            }

            foreach (var bulletId in bulletIds)
            {
                if (variantCountByBullet.TryGetValue(bulletId, out var count) && count > 0)
                {
                    commands.Add(new SelectVariantCommand
                    {
                        Target = bulletId,
                        VariantIndex = 0,
                        Rationale = "An existing phrasing matches without spending generation tokens.",
                    });
                }
            }
        }
    }

    private static void AppendEmphasizeSkills(List<TailorCommand> commands, List<string> skillIds)
    {
        if (skillIds.Count == 0)
        {
            return;
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var skillId in skillIds)
        {
            if (EntityId.TryParse(skillId, out var id) && id.SubKey is { } subKey && seen.Add(subKey))
            {
                normalized.Add(subKey);
            }
        }

        if (normalized.Count > 0)
        {
            commands.Add(new EmphasizeSkillsCommand
            {
                Skills = normalized,
                Rationale = "Matches skills the job description asks for.",
            });
        }
    }

    /// <summary>
    /// Resolves every unresolved field in <paramref name="brief"/> (the text
    /// <c>AutofillEndpoints.BuildBrief</c> produces) to a canonical key by
    /// <see cref="AutofillFieldMatcher"/>, populating <c>optionValue</c> for select/radio
    /// fields by fuzzy-matching the candidate's own knowledge-base value against the
    /// field's options.
    /// </summary>
    private async Task<List<HeuristicFieldResolution>> ResolveFieldsAsync(string brief, CancellationToken ct)
    {
        var (canonicalKeys, fields) = ParseFieldResolutionBrief(brief);
        var resolutions = new List<HeuristicFieldResolution>(fields.Count);
        if (fields.Count == 0)
        {
            return resolutions;
        }

        IReadOnlyDictionary<string, string>? profileValues = null;

        foreach (var field in fields)
        {
            var (canonicalKey, confidence) = AutofillFieldMatcher.ResolveBestKey(
                canonicalKeys, field.Label, field.Name, field.Placeholder, field.AutoComplete);

            string? optionValue = null;
            var isChoiceInput = string.Equals(field.InputType, "select", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field.InputType, "radio", StringComparison.OrdinalIgnoreCase);

            if (canonicalKey.Length > 0 && isChoiceInput && field.Options.Count > 0)
            {
                profileValues ??= await BuildProfileValuesAsync(ct).ConfigureAwait(false);
                if (profileValues.TryGetValue(canonicalKey, out var profileValue))
                {
                    optionValue = AutofillFieldMatcher.FuzzyMatchOption(field.Options, profileValue);
                }
            }

            resolutions.Add(new HeuristicFieldResolution
            {
                ElementId = field.ElementId,
                CanonicalKey = canonicalKey,
                Confidence = confidence,
                OptionValue = optionValue,
            });
        }

        return resolutions;
    }

    /// <summary>
    /// Parses the pipe-delimited brief <c>AutofillEndpoints.BuildBrief</c> emits:
    /// <c>CANONICAL-KEYS: k1,k2,...</c>, a <c>HOST:</c> line (unused here — the heuristic
    /// resolves purely from field text), then a <c>FIELDS</c> section of
    /// <c>elementId|inputType|label|name|placeholder|autocomplete|opt1;opt2</c> lines.
    /// </summary>
    private static (List<string> CanonicalKeys, List<UnresolvedFieldBrief> Fields) ParseFieldResolutionBrief(string brief)
    {
        var canonicalKeys = new List<string>();
        var fields = new List<UnresolvedFieldBrief>();
        var inFields = false;

        foreach (var rawLine in brief.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(CanonicalKeysPrefix, StringComparison.Ordinal))
            {
                var keys = line[CanonicalKeysPrefix.Length..].Trim();
                if (keys.Length > 0)
                {
                    canonicalKeys.AddRange(keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }

                inFields = false;
                continue;
            }

            if (line.StartsWith(HostPrefix, StringComparison.Ordinal))
            {
                inFields = false;
                continue;
            }

            if (line == FieldsHeader)
            {
                inFields = true;
                continue;
            }

            if (!inFields)
            {
                continue;
            }

            var parts = line.Split('|', 7);
            if (parts.Length != 7)
            {
                continue;
            }

            fields.Add(new UnresolvedFieldBrief(
                ElementId: parts[0],
                InputType: parts[1],
                Label: parts[2].Length == 0 ? null : parts[2],
                Name: parts[3].Length == 0 ? null : parts[3],
                Placeholder: parts[4].Length == 0 ? null : parts[4],
                AutoComplete: parts[5].Length == 0 ? null : parts[5],
                Options: parts[6].Length == 0 ? [] : parts[6].Split(';', StringSplitOptions.RemoveEmptyEntries)));
        }

        return (canonicalKeys, fields);
    }

    /// <summary>
    /// Builds a canonical-key → value map from the knowledge base, mirroring the subset of
    /// canonical keys <c>AutofillEndpoints.GetProfileAsync</c> populates (Infrastructure has
    /// no reference to the Api layer, so this cannot call that code directly). Only keys
    /// with a real value are present.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> BuildProfileValuesAsync(CancellationToken ct)
    {
        var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
        var basics = snapshot.Basics;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        var (firstName, lastName) = SplitName(basics.FullName);
        Set("firstName", firstName);
        Set("lastName", lastName);
        Set("fullName", basics.FullName);
        Set("email", basics.Email);
        Set("phone", basics.Phone);
        Set("linkedin", basics.LinkedIn);
        Set("github", basics.GitHub);
        Set("website", basics.Website);
        Set("portfolio", basics.Website);
        Set("currentTitle", basics.Headline);

        return values;
    }

    private static (string? FirstName, string? LastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (null, null);
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[^1]),
        };
    }

    private sealed record UnresolvedFieldBrief(
        string ElementId, string InputType, string? Label, string? Name, string? Placeholder, string? AutoComplete, IReadOnlyList<string> Options);

    /// <summary>
    /// Wire shape matching the <c>"field-resolutions"</c> JSON schema
    /// (<see cref="JsonSchemaRegistry"/>) — deliberately not the Api layer's
    /// <c>FieldResolution</c> contract type, which Infrastructure cannot reference.
    /// Round-tripped through JSON like <see cref="TailorCommand"/> above, so it
    /// deserializes correctly into whatever concrete type the caller requests.
    /// </summary>
    private sealed record HeuristicFieldResolution
    {
        public required string ElementId { get; init; }

        public required string CanonicalKey { get; init; }

        public required double Confidence { get; init; }

        public string? OptionValue { get; init; }
    }
}
