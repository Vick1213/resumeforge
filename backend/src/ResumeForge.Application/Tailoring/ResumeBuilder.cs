using System.Globalization;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using ResumeForge.Domain.Text;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="IResumeBuilder"/>. Entries are sorted with current roles
/// first, then by end date descending; bullet ids are assigned once, in file order, as
/// <c>{parent}#{ordinal}</c>. Per CONTRACTS.md §3, there is no authored skills file —
/// skill groups are synthesized from the <c>tech:</c> frontmatter of every experience and
/// project item, canonicalized through <see cref="SkillNormalizer"/> and
/// <see cref="ISkillTaxonomy"/>, and bucketed one group per taxonomy category.
/// </summary>
public sealed class ResumeBuilder(ISkillTaxonomy taxonomy, TimeProvider timeProvider) : IResumeBuilder
{
    private const string OtherCategory = "other";
    private const string OtherLabel = "Other";

    private static readonly string[] CategoryOrder =
        ["languages", "frameworks", "datastores", "cloud", "practices", "tools", "soft"];

    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.Ordinal)
    {
        ["languages"] = "Languages",
        ["frameworks"] = "Frameworks",
        ["datastores"] = "Datastores",
        ["cloud"] = "Cloud & Infrastructure",
        ["practices"] = "Practices",
        ["tools"] = "Tools",
        ["soft"] = "Soft Skills",
    };

    /// <inheritdoc />
    public ResumeDocument Build(KnowledgeBaseSnapshot snapshot, string? id = null, string name = "Base resume")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var experienceItems = OrderItems(snapshot.Items.Where(i => i.Type == KnowledgeItemType.Experience));
        var projectItems = OrderItems(snapshot.Items.Where(i => i.Type == KnowledgeItemType.Project));
        var educationItems = OrderItems(snapshot.Items.Where(i => i.Type == KnowledgeItemType.Education));
        var certificationItems = OrderItems(snapshot.Items.Where(i => i.Type == KnowledgeItemType.Certification));

        var now = timeProvider.GetUtcNow();

        return new ResumeDocument
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            Basics = snapshot.Basics,
            Summary = snapshot.DefaultSummary,
            Skills = BuildSkillGroups(experienceItems.Concat(projectItems)),
            Experience = [.. experienceItems.Select(BuildExperienceEntry)],
            Projects = [.. projectItems.Select(BuildProjectEntry)],
            Education = [.. educationItems.Select(BuildEducationEntry)],
            Certifications = [.. certificationItems.Select(BuildCertificationEntry)],
            SectionOrder =
            [
                SectionKind.Summary,
                SectionKind.Skills,
                SectionKind.Experience,
                SectionKind.Projects,
                SectionKind.Education,
                SectionKind.Certifications,
            ],
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static List<KnowledgeItem> OrderItems(IEnumerable<KnowledgeItem> items) =>
        [.. items
            .OrderByDescending(SortPrimaryDate)
            .ThenByDescending(i => i.StartDate ?? DateOnly.MinValue)
            .ThenBy(i => i.Slug, StringComparer.Ordinal)];

    private static DateOnly SortPrimaryDate(KnowledgeItem item) =>
        item.IsCurrent ? DateOnly.MaxValue : item.EndDate ?? item.StartDate ?? DateOnly.MinValue;

    private static ExperienceEntry BuildExperienceEntry(KnowledgeItem item) => new()
    {
        Id = item.Id.ToString(),
        Role = item.Title,
        Organization = item.Organization ?? string.Empty,
        Location = item.Extra.GetValueOrDefault("location"),
        StartDate = item.StartDate ?? DateOnly.MinValue,
        EndDate = item.IsCurrent ? null : item.EndDate,
        Bullets = BuildBullets(item),
        Tech = item.Tech,
        Included = true,
    };

    private static ProjectEntry BuildProjectEntry(KnowledgeItem item) => new()
    {
        Id = item.Id.ToString(),
        Name = item.Title,
        Tagline = item.Extra.GetValueOrDefault("tagline"),
        Url = item.Extra.GetValueOrDefault("url"),
        RepoUrl = item.Extra.GetValueOrDefault("repoUrl"),
        StartDate = item.StartDate,
        EndDate = item.IsCurrent ? null : item.EndDate,
        Bullets = BuildBullets(item),
        Tech = item.Tech,
        Included = true,
    };

    private static EducationEntry BuildEducationEntry(KnowledgeItem item) => new()
    {
        Id = item.Id.ToString(),
        Institution = item.Organization ?? item.Title,
        Credential = item.Title,
        StartDate = item.StartDate,
        EndDate = item.IsCurrent ? null : item.EndDate,
        Location = item.Extra.GetValueOrDefault("location"),
        Gpa = TryParseGpa(item.Extra),
        Highlights = [.. item.Bullets.Select(b => b.Text)],
        Included = true,
    };

    private static CertificationEntry BuildCertificationEntry(KnowledgeItem item) => new()
    {
        Id = item.Id.ToString(),
        Name = item.Title,
        Issuer = item.Organization,
        IssuedOn = item.StartDate ?? item.EndDate,
        CredentialUrl = item.Extra.GetValueOrDefault("credentialUrl") ?? item.Extra.GetValueOrDefault("url"),
        Included = true,
    };

    private static List<Bullet> BuildBullets(KnowledgeItem item)
    {
        var result = new List<Bullet>(item.Bullets.Count);

        for (var ordinal = 0; ordinal < item.Bullets.Count; ordinal++)
        {
            var kb = item.Bullets[ordinal];
            result.Add(new Bullet
            {
                Id = item.Id.Child(ordinal).ToString(),
                Text = kb.Text,
                Variants = kb.Variants,
                Tags = kb.Tags,
                Relevance = 0,
            });
        }

        return result;
    }

    private static double? TryParseGpa(IReadOnlyDictionary<string, string> extra) =>
        extra.TryGetValue("gpa", out var raw) &&
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private List<SkillGroup> BuildSkillGroups(IEnumerable<KnowledgeItem> sourceItems)
    {
        var byCategory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var item in sourceItems)
        {
            foreach (var rawTech in item.Tech)
            {
                var normalized = SkillNormalizer.Normalize(rawTech);
                if (normalized.Length == 0)
                {
                    continue;
                }

                string category;
                string canonical;

                if (taxonomy.TryCanonicalize(normalized, out var canon))
                {
                    canonical = canon;
                    category = taxonomy.CategoryOf(canon);
                    if (string.IsNullOrEmpty(category))
                    {
                        category = OtherCategory;
                    }
                }
                else
                {
                    canonical = normalized;
                    category = OtherCategory;
                }

                if (!byCategory.TryGetValue(category, out var bucket))
                {
                    bucket = new Dictionary<string, string>(StringComparer.Ordinal);
                    byCategory[category] = bucket;
                }

                if (!bucket.ContainsKey(canonical))
                {
                    bucket[canonical] = rawTech.Trim();
                }
            }
        }

        var groups = new List<SkillGroup>();

        foreach (var category in CategoryOrder)
        {
            if (byCategory.TryGetValue(category, out var bucket) && bucket.Count > 0)
            {
                groups.Add(BuildGroup(category, CategoryLabels[category], bucket));
            }
        }

        if (byCategory.TryGetValue(OtherCategory, out var otherBucket) && otherBucket.Count > 0)
        {
            groups.Add(BuildGroup(OtherCategory, OtherLabel, otherBucket));
        }

        return groups;
    }

    private static SkillGroup BuildGroup(string category, string label, Dictionary<string, string> canonicalToDisplayName)
    {
        var groupId = EntityId.Parse($"skl:{category}");

        var items = canonicalToDisplayName
            .OrderBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(kv => new Skill
            {
                Id = groupId.Child(kv.Key).ToString(),
                Name = kv.Value,
                Normalized = kv.Key,
                Emphasized = false,
            })
            .ToList();

        return new SkillGroup
        {
            Id = groupId.ToString(),
            Label = label,
            Items = items,
            Included = true,
        };
    }
}
