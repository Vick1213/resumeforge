using System.Text.Json;
using System.Text.RegularExpressions;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Text;

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
/// for a given brief. At <see cref="ModelEffort.Thorough"/> and above (conveyed by the
/// brief's <c>EFFORT|</c> line and <c>KEYWORDS</c> section — see <see cref="BriefBuilder"/>)
/// it also considers proposing <see cref="InjectKeywordsCommand"/>s, but only where it can
/// do so honestly: a keyword is placed on a bullet only when that specific bullet's own
/// entry lists the technology (frontmatter <c>tech:</c>) and the bullet's own text already
/// names it under a different spelling (e.g. "K8s" for "kubernetes") — in which case the
/// injected text spells the abbreviation out in place, exactly as a person clarifying their
/// own sentence would, never as an appended sentence. A keyword with no bullet that clears
/// both bars is not forced onto an unrelated one; it is simply not proposed (see
/// <see cref="AppendKeywordInjections"/>). A digit-bearing keyword, or one whose display
/// name carries a digit, is skipped outright, since inserting either would register as a
/// fabricated new number under <see cref="IFabricationGuard"/>.
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
public sealed class HeuristicLanguageModel(TailorOptions tailorOptions, IKnowledgeBaseReader knowledgeBaseReader, ISkillTaxonomy skillTaxonomy) : ILanguageModel
{
    private const string RequirementsHeader = "REQUIREMENTS";
    private const string ExperienceHeader = "CANDIDATES-EXPERIENCE";
    private const string ProjectsHeader = "CANDIDATES-PROJECTS";
    private const string SkillsHeader = "CANDIDATES-SKILLS";
    private const string KeywordsHeader = "KEYWORDS";
    private const string EffortPrefix = "EFFORT|";

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
            JsonSchemaRegistry.TailorCommandsSchemaName => await BuildCommandsAsync(request.User, ct).ConfigureAwait(false),
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

    private async Task<List<TailorCommand>> BuildCommandsAsync(string brief, CancellationToken ct)
    {
        var parsed = ParseTailorBrief(brief);

        var commands = new List<TailorCommand>();
        var keptExperienceBullets = AppendEntryCommands(commands, parsed.Experience, tailorOptions.MaxExperienceEntries);
        var keptProjectBullets = AppendEntryCommands(commands, parsed.Projects, tailorOptions.MaxProjectEntries);
        AppendEmphasizeSkills(commands, parsed.SkillIds);

        if (parsed.Effort >= ModelEffort.Thorough && parsed.Keywords.Count > 0)
        {
            var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
            var fullTextByBulletId = BuildFullBulletTextLookup(snapshot);

            // Prefer the KB's own untruncated bullet text over the brief's candidate text:
            // BriefBuilder truncates candidate text to 140 characters for the model's token
            // economy, and building the rewritten bullet from that truncated preview would
            // silently shorten (and ellipsis-mangle) any bullet longer than that.
            var keptBullets = new List<(string Id, string Text)>(keptExperienceBullets.Count + keptProjectBullets.Count);
            foreach (var (id, text) in keptExperienceBullets.Concat(keptProjectBullets))
            {
                keptBullets.Add((id, fullTextByBulletId.GetValueOrDefault(id, text)));
            }

            AppendKeywordInjections(commands, keptBullets, parsed.Keywords, snapshot, skillTaxonomy);
        }

        return commands;
    }

    private sealed record ParsedCandidate(string Id, int VariantCount, string Text);

    private sealed record ParsedTailorBrief(
        List<ParsedCandidate> Experience, List<ParsedCandidate> Projects, List<string> SkillIds, ModelEffort Effort, List<string> Keywords);

    private static ParsedTailorBrief ParseTailorBrief(string brief)
    {
        var experience = new List<ParsedCandidate>();
        var projects = new List<ParsedCandidate>();
        var skills = new List<string>();
        var keywords = new List<string>();
        var effort = ModelEffort.Standard;
        var section = string.Empty;

        foreach (var rawLine in brief.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(EffortPrefix, StringComparison.Ordinal))
            {
                effort = ParseEffort(line[EffortPrefix.Length..]);
                section = string.Empty;
                continue;
            }

            if (line is RequirementsHeader or ExperienceHeader or ProjectsHeader or SkillsHeader or KeywordsHeader)
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

                var text = parts.Length > 2 ? parts[2] : string.Empty;
                (section == ExperienceHeader ? experience : projects).Add(new ParsedCandidate(parts[0], variantCount, text));
            }
            else if (section == SkillsHeader)
            {
                var parts = line.Split('|', 2);
                if (parts.Length >= 1 && parts[0].Length > 0)
                {
                    skills.Add(parts[0]);
                }
            }
            else if (section == KeywordsHeader)
            {
                keywords.Add(line);
            }
        }

        return new ParsedTailorBrief(experience, projects, skills, effort, keywords);
    }

    private static ModelEffort ParseEffort(string token) => token switch
    {
        "minimal" => ModelEffort.Minimal,
        "standard" => ModelEffort.Standard,
        "thorough" => ModelEffort.Thorough,
        "maximum" => ModelEffort.Maximum,
        _ => ModelEffort.Standard,
    };

    /// <summary>
    /// Adds include/exclude/order/selectVariant commands for <paramref name="candidates"/>
    /// exactly as before, and returns the kept bullets (id, original truncated text) in kept
    /// order, so a caller can weave keyword injections into them without re-deriving which
    /// entries survived the cap.
    /// </summary>
    private static List<(string Id, string Text)> AppendEntryCommands(List<TailorCommand> commands, List<ParsedCandidate> candidates, int maxEntries)
    {
        var keptBullets = new List<(string, string)>();
        if (candidates.Count == 0)
        {
            return keptBullets;
        }

        var entryOrder = new List<string>();
        var seenEntries = new HashSet<string>(StringComparer.Ordinal);
        var bulletsByEntry = new Dictionary<string, List<ParsedCandidate>>(StringComparer.Ordinal);
        var seenBulletsByEntry = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (!EntityId.TryParse(candidate.Id, out var id))
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

            if (seenBulletsByEntry[entryId].Add(candidate.Id))
            {
                bulletsByEntry[entryId].Add(candidate);
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
            var bulletCandidates = bulletsByEntry[entryId];
            var bulletIds = bulletCandidates.Select(b => b.Id).ToList();

            if (bulletIds.Count > 1)
            {
                commands.Add(new OrderCommand { Parent = entryId, Order = bulletIds, Rationale = "Bullets ordered by relevance to the job description." });
            }

            foreach (var candidate in bulletCandidates)
            {
                if (candidate.VariantCount > 0)
                {
                    commands.Add(new SelectVariantCommand
                    {
                        Target = candidate.Id,
                        VariantIndex = 0,
                        Rationale = "An existing phrasing matches without spending generation tokens.",
                    });
                }

                keptBullets.Add((candidate.Id, candidate.Text));
            }
        }

        return keptBullets;
    }

    /// <summary>
    /// Proposes an <see cref="InjectKeywordsCommand"/> for a keyword only where it can be
    /// placed honestly, on a bullet it is actually about — never round-robined onto whatever
    /// bullet happens to be next, which is how a Kafka mention used to land on a sentence
    /// about mentoring engineers. A keyword clears the bar for a candidate bullet only when
    /// <em>both</em> hold: the bullet's own parent entry lists the technology in its
    /// frontmatter <c>tech:</c> (<see cref="ItemEvidencesKeyword"/>), and the bullet's own
    /// text already names that technology under a different spelling the taxonomy knows about
    /// — "K8s" for "kubernetes", say (<see cref="TryBuildNaturalKeywordText"/>). That second
    /// requirement is what keeps this from being a rename of the old defect with a smaller
    /// blast radius: entry-level <c>tech:</c> alone still spans every bullet in a role, so
    /// pairing it with the bullet's own wording is what actually ties the keyword to that one
    /// bullet instead of that whole job. When a bullet clears both bars, the injected text is
    /// the original sentence with the display spelling parenthesized in place — never an
    /// appended sentence — so it reads as something the person themselves clarified. A
    /// keyword that carries a digit, or whose display name does, is skipped outright
    /// (inserting either would register as a fabricated new number under
    /// <see cref="IFabricationGuard"/>), and a keyword with no bullet clearing both bars is
    /// simply not proposed at all — CONTRACTS.md §6's "emit nothing for it rather than
    /// forcing it" — including, honestly, most keywords most of the time: this heuristic has
    /// no real language understanding, so it can only ever catch the narrow case where the
    /// person's own bullet already used an abbreviation. A real model is what buys the rest.
    /// </summary>
    private static void AppendKeywordInjections(
        List<TailorCommand> commands, List<(string Id, string Text)> keptBullets, List<string> keywords,
        KnowledgeBaseSnapshot snapshot, ISkillTaxonomy taxonomy)
    {
        if (keptBullets.Count == 0)
        {
            return;
        }

        var itemByBulletId = BuildBulletItemLookup(snapshot);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var keyword in keywords)
        {
            if (keyword.Length == 0 || keyword.Any(char.IsAsciiDigit))
            {
                continue;
            }

            if (!IsKeywordEvidenced(keyword, snapshot))
            {
                continue;
            }

            foreach (var (id, text) in keptBullets)
            {
                if (used.Contains(id) || SkillNormalizer.Normalize(text).Contains(keyword, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!itemByBulletId.TryGetValue(id, out var item) || !ItemEvidencesKeyword(item, keyword))
                {
                    continue;
                }

                if (!TryBuildNaturalKeywordText(text, keyword, taxonomy, out var injectedText))
                {
                    continue;
                }

                commands.Add(new InjectKeywordsCommand
                {
                    Target = id,
                    Keywords = [keyword],
                    Text = injectedText,
                    Rationale = "Spells out an abbreviation this bullet already uses in the form keyword scanners look for.",
                });

                used.Add(id);
                break;
            }
        }
    }

    /// <summary>True when <paramref name="item"/>'s own frontmatter <c>tech:</c> list names <paramref name="keyword"/>.</summary>
    private static bool ItemEvidencesKeyword(KnowledgeItem item, string keyword) =>
        item.Tech.Any(t => string.Equals(SkillNormalizer.Normalize(t), keyword, StringComparison.Ordinal));

    /// <summary>
    /// Attempts to build injected bullet text that reads as something the candidate wrote:
    /// finds a whole-word occurrence, in <paramref name="bulletText"/> itself, of some other
    /// spelling the taxonomy lists for <paramref name="keyword"/> (an abbreviation like "K8s"
    /// or "Postgres") and parenthesizes the taxonomy's display spelling immediately after it
    /// — "K8s (Kubernetes)" — leaving every other word, and therefore every number and proper
    /// noun <see cref="IFabricationGuard"/> checks for, untouched. Returns false (with no
    /// output) whenever no such occurrence exists, whenever <paramref name="keyword"/> isn't
    /// a taxonomy entry at all, or whenever the display spelling itself carries a digit —
    /// this is deliberately narrow rather than templated, since a phrase stamped onto every
    /// bullet regardless of content is exactly the filler this replaces.
    /// </summary>
    private static bool TryBuildNaturalKeywordText(string bulletText, string keyword, ISkillTaxonomy taxonomy, out string injectedText)
    {
        injectedText = string.Empty;

        if (!taxonomy.AllCanonical.Contains(keyword))
        {
            return false;
        }

        var display = taxonomy.DisplayNameOf(keyword);
        if (display.Length == 0 || display.Any(char.IsAsciiDigit))
        {
            return false;
        }

        foreach (var alias in taxonomy.AliasesFor(keyword))
        {
            var trimmedAlias = alias.Trim();
            if (trimmedAlias.Length == 0 ||
                string.Equals(trimmedAlias, keyword, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedAlias, display, StringComparison.OrdinalIgnoreCase))
            {
                // The keyword's own spelling (or its display form) appearing verbatim is the
                // trivial "already mentions it" case, handled by the caller before this is
                // ever called — not a distinct abbreviation worth annotating.
                continue;
            }

            if (!TryFindWholeWordOccurrence(bulletText, trimmedAlias, out var match))
            {
                continue;
            }

            var insertAt = match.Index + match.Length;
            injectedText = string.Concat(bulletText.AsSpan(0, insertAt), $" ({display})", bulletText.AsSpan(insertAt));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first case-insensitive occurrence of <paramref name="phrase"/> in
    /// <paramref name="text"/> that is bounded by non-alphanumeric characters (or the ends of
    /// the string) on both sides, so a short alias like "cs" or "tf" can only match a real
    /// standalone word — never a substring buried inside an unrelated one.
    /// </summary>
    private static bool TryFindWholeWordOccurrence(string text, string phrase, out Match match)
    {
        var pattern = $"(?<![A-Za-z0-9]){Regex.Escape(phrase)}(?![A-Za-z0-9])";
        match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success;
    }

    /// <summary>Maps every bullet id to the <see cref="KnowledgeItem"/> it belongs to, for looking up that entry's own <c>tech:</c> list.</summary>
    private static Dictionary<string, KnowledgeItem> BuildBulletItemLookup(KnowledgeBaseSnapshot snapshot)
    {
        var lookup = new Dictionary<string, KnowledgeItem>(StringComparer.Ordinal);

        foreach (var item in snapshot.Items)
        {
            for (var ordinal = 0; ordinal < item.Bullets.Count; ordinal++)
            {
                lookup[item.Id.Child(ordinal).ToString()] = item;
            }
        }

        return lookup;
    }

    /// <summary>
    /// Maps every bullet id (<c>exp:acme#0</c>, <c>prj:graph-runner#0</c>, ...) to its full,
    /// untruncated text straight from the knowledge base — the same ids
    /// <see cref="ResumeForge.Application.Tailoring.ResumeBuilder"/> assigns, since both
    /// walk each item's bullets in file order.
    /// </summary>
    private static Dictionary<string, string> BuildFullBulletTextLookup(KnowledgeBaseSnapshot snapshot)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in snapshot.Items)
        {
            for (var ordinal = 0; ordinal < item.Bullets.Count; ordinal++)
            {
                lookup[item.Id.Child(ordinal).ToString()] = item.Bullets[ordinal].Text;
            }
        }

        return lookup;
    }

    private static bool IsKeywordEvidenced(string keyword, KnowledgeBaseSnapshot snapshot)
    {
        foreach (var item in snapshot.Items)
        {
            foreach (var tech in item.Tech)
            {
                if (string.Equals(SkillNormalizer.Normalize(tech), keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (TextEvidences(item.Title, keyword) || TextEvidences(item.Organization, keyword))
            {
                return true;
            }

            foreach (var bullet in item.Bullets)
            {
                if (TextEvidences(bullet.Text, keyword))
                {
                    return true;
                }

                foreach (var variant in bullet.Variants)
                {
                    if (TextEvidences(variant, keyword))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TextEvidences(string? text, string normalizedKeyword) =>
        !string.IsNullOrWhiteSpace(text) && SkillNormalizer.Normalize(text).Contains(normalizedKeyword, StringComparison.Ordinal);

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
    /// <see cref="AutofillFieldMatcher"/>. Per CONTRACTS.md §10's effort table, select/radio
    /// <c>optionValue</c> choice is proposed only at <see cref="ModelEffort.Standard"/> and
    /// above (fuzzy-matched from the candidate's own knowledge-base value); free-text answers
    /// for open questions are a real-model capability this offline heuristic never attempts,
    /// the same way it never emits a resume <see cref="RewriteCommand"/>, so those fields
    /// simply come back with a null <c>optionValue</c> regardless of effort.
    /// </summary>
    private async Task<List<HeuristicFieldResolution>> ResolveFieldsAsync(string brief, CancellationToken ct)
    {
        var (canonicalKeys, fields, effort) = ParseFieldResolutionBrief(brief);
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

            if (canonicalKey.Length > 0 && isChoiceInput && field.Options.Count > 0 && effort >= ModelEffort.Standard)
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
    /// Parses the pipe-delimited brief <c>AutofillEndpoints.BuildBrief</c> emits: an optional
    /// <c>EFFORT|</c> line (absent means <see cref="ModelEffort.Standard"/>, matching the
    /// wire contract's default), <c>CANONICAL-KEYS: k1,k2,...</c>, a <c>HOST:</c> line
    /// (unused here — the heuristic resolves purely from field text), then a <c>FIELDS</c>
    /// section of <c>elementId|inputType|label|name|placeholder|autocomplete|opt1;opt2</c>
    /// lines.
    /// </summary>
    private static (List<string> CanonicalKeys, List<UnresolvedFieldBrief> Fields, ModelEffort Effort) ParseFieldResolutionBrief(string brief)
    {
        var canonicalKeys = new List<string>();
        var fields = new List<UnresolvedFieldBrief>();
        var effort = ModelEffort.Standard;
        var inFields = false;

        foreach (var rawLine in brief.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(EffortPrefix, StringComparison.Ordinal))
            {
                effort = ParseEffort(line[EffortPrefix.Length..]);
                inFields = false;
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

        return (canonicalKeys, fields, effort);
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
