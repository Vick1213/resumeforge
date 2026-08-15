using System.Text.Json;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Ids;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// No-network <see cref="ILanguageModel"/> used whenever no Anthropic API key is
/// configured, so the product works end to end without one. Parses the compact brief
/// produced by <c>BriefBuilder</c> and proposes commands by pure ranking rules: the
/// brief's candidate order already reflects relevance (CONTRACTS.md §6, §7), so the
/// heuristic keeps the top-scored entries within <see cref="TailorOptions"/>'s caps,
/// excludes the rest, orders each kept entry's bullets by that same relevance order,
/// selects an existing variant over generating prose whenever one is available, and
/// emphasizes the skills the job description's candidate set surfaced. It never emits a
/// <see cref="RewriteCommand"/> and its output is deterministic for a given brief.
/// </summary>
public sealed class HeuristicLanguageModel(TailorOptions tailorOptions) : ILanguageModel
{
    private const string RequirementsHeader = "REQUIREMENTS";
    private const string ExperienceHeader = "CANDIDATES-EXPERIENCE";
    private const string ProjectsHeader = "CANDIDATES-PROJECTS";
    private const string SkillsHeader = "CANDIDATES-SKILLS";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public string ModelId => "heuristic-v1";

    /// <inheritdoc />
    public Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!string.Equals(request.SchemaName, JsonSchemaRegistry.TailorCommandsSchemaName, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{nameof(HeuristicLanguageModel)} does not support schema '{request.SchemaName}'; it only proposes '{JsonSchemaRegistry.TailorCommandsSchemaName}'.");
        }

        var commands = BuildCommands(request.User);

        // Round-trip through JSON rather than casting directly, so this works for any T the
        // caller requests for this schema (a list, an array, etc.) the same way a real
        // model response would be deserialized.
        var json = JsonSerializer.Serialize<IReadOnlyList<TailorCommand>>(commands, SerializerOptions);
        var value = JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Heuristic completion produced a null value.");

        return Task.FromResult(new ModelResponse<T>
        {
            Value = value,
            Usage = TokenUsage.Empty,
            FromCache = false,
        });
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
}
