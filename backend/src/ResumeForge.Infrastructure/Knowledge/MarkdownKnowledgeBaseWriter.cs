using System.Text;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Knowledge;

/// <summary>
/// Filesystem <see cref="IKnowledgeBaseWriter"/> for the markdown knowledge base. Emits
/// frontmatter keys in the canonical order documented in CONTRACTS.md §3 so that reading a
/// file and writing it straight back is byte-identical: known fields occupy their
/// documented position, and any genuinely unrecognized <see cref="KnowledgeItem.Extra"/>
/// keys are appended afterward, sorted for determinism.
/// </summary>
public sealed class MarkdownKnowledgeBaseWriter(IProfileRootProvider profileRootProvider) : IKnowledgeBaseWriter
{
    /// <inheritdoc />
    public async Task WriteAsync(KnowledgeItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        var root = profileRootProvider.ProfileRoot;
        var dir = Path.Combine(root, DirectoryName(item.Type));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, item.Slug + ".md");
        var content = Render(item);

        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(EntityId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var root = profileRootProvider.ProfileRoot;
        var dir = Path.Combine(root, DirectoryNameForKind(id.Kind));
        var path = Path.Combine(dir, id.Slug + ".md");

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteBasicsAsync(ResumeBasics basics, string? summary, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(basics);

        var root = profileRootProvider.ProfileRoot;
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "basics.md");
        var content = RenderBasics(basics, summary);

        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
    }

    private static string Render(KnowledgeItem item)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append('\n');

        var order = item.Type switch
        {
            KnowledgeItemType.Experience => KnowledgeMarkdownFormat.ExperienceFieldOrder,
            KnowledgeItemType.Project => KnowledgeMarkdownFormat.ProjectFieldOrder,
            KnowledgeItemType.Education => KnowledgeMarkdownFormat.EducationFieldOrder,
            KnowledgeItemType.Certification => KnowledgeMarkdownFormat.CertificationFieldOrder,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Type, "Unsupported knowledge item type."),
        };

        Line("---");
        Line($"type: {KnowledgeMarkdownFormat.TypeLiteral(item.Type)}");

        var knownKeys = new HashSet<string>(order, StringComparer.Ordinal);
        foreach (var key in order)
        {
            var value = GetFieldValue(item, key);
            if (value is not null)
            {
                Line($"{key}: {value}");
            }
        }

        foreach (var (key, value) in item.Extra.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!knownKeys.Contains(key))
            {
                Line($"{key}: {EmitExtraValue(value)}");
            }
        }

        Line("---");

        if (item.Type != KnowledgeItemType.Certification)
        {
            Line(string.Empty);
            foreach (var bullet in item.Bullets)
            {
                Line($"- {bullet.Text}");
                foreach (var variant in bullet.Variants)
                {
                    Line($"  - {variant}");
                }
            }
        }

        return sb.ToString();
    }

    private static string? GetFieldValue(KnowledgeItem item, string key) => item.Type switch
    {
        KnowledgeItemType.Experience => key switch
        {
            "role" => KnowledgeMarkdownFormat.FormatScalar(item.Title),
            "organization" => item.Organization is { } o ? KnowledgeMarkdownFormat.FormatScalar(o) : null,
            "location" => FromExtra(item, "location"),
            "startDate" => KnowledgeMarkdownFormat.FormatDate(item.StartDate, isCurrent: false),
            "endDate" => KnowledgeMarkdownFormat.FormatDate(item.EndDate, item.IsCurrent),
            "tech" => item.Tech.Count > 0 ? KnowledgeMarkdownFormat.FormatInlineList(item.Tech) : null,
            "tags" => item.Tags.Count > 0 ? KnowledgeMarkdownFormat.FormatInlineList(item.Tags) : null,
            _ => null,
        },
        KnowledgeItemType.Project => key switch
        {
            "name" => KnowledgeMarkdownFormat.FormatScalar(item.Title),
            "tagline" => FromExtra(item, "tagline"),
            "url" => FromExtra(item, "url"),
            "repoUrl" => FromExtra(item, "repoUrl"),
            "startDate" => KnowledgeMarkdownFormat.FormatDate(item.StartDate, isCurrent: false),
            "endDate" => KnowledgeMarkdownFormat.FormatDate(item.EndDate, item.IsCurrent),
            "tech" => item.Tech.Count > 0 ? KnowledgeMarkdownFormat.FormatInlineList(item.Tech) : null,
            "tags" => item.Tags.Count > 0 ? KnowledgeMarkdownFormat.FormatInlineList(item.Tags) : null,
            "source" => item.Source == KnowledgeSource.GitHub ? "github" : "manual",
            "stars" => FromExtra(item, "stars"),
            _ => null,
        },
        KnowledgeItemType.Education => key switch
        {
            "institution" => item.Organization is { } o ? KnowledgeMarkdownFormat.FormatScalar(o) : null,
            "credential" => KnowledgeMarkdownFormat.FormatScalar(item.Title),
            "location" => FromExtra(item, "location"),
            "startDate" => KnowledgeMarkdownFormat.FormatDate(item.StartDate, isCurrent: false),
            "endDate" => KnowledgeMarkdownFormat.FormatDate(item.EndDate, item.IsCurrent),
            "gpa" => FromExtra(item, "gpa"),
            _ => null,
        },
        KnowledgeItemType.Certification => key switch
        {
            "name" => KnowledgeMarkdownFormat.FormatScalar(item.Title),
            "issuer" => item.Organization is { } o ? KnowledgeMarkdownFormat.FormatScalar(o) : null,
            "issuedOn" => KnowledgeMarkdownFormat.FormatDate(item.StartDate, isCurrent: false),
            "credentialUrl" => FromExtra(item, "credentialUrl"),
            _ => null,
        },
        _ => null,
    };

    private static string? FromExtra(KnowledgeItem item, string key) =>
        item.Extra.TryGetValue(key, out var raw) ? EmitExtraValue(raw) : null;

    private static string EmitExtraValue(string raw) =>
        raw.StartsWith('[') && raw.EndsWith(']') ? raw : KnowledgeMarkdownFormat.FormatScalar(raw);

    private static string RenderBasics(ResumeBasics basics, string? summary)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append('\n');

        Line("---");
        Line($"fullName: {KnowledgeMarkdownFormat.FormatScalar(basics.FullName)}");
        AppendIfPresent(Line, "headline", basics.Headline);
        AppendIfPresent(Line, "email", basics.Email);
        AppendIfPresent(Line, "phone", basics.Phone);
        AppendIfPresent(Line, "location", basics.Location);
        AppendIfPresent(Line, "website", basics.Website);
        AppendIfPresent(Line, "linkedin", basics.LinkedIn);
        AppendIfPresent(Line, "github", basics.GitHub);
        Line("---");

        if (summary is not null)
        {
            Line(string.Empty);
            foreach (var summaryLine in summary.Split('\n'))
            {
                Line(summaryLine);
            }
        }

        return sb.ToString();
    }

    private static void AppendIfPresent(Action<string> line, string key, string? value)
    {
        if (value is not null)
        {
            line($"{key}: {KnowledgeMarkdownFormat.FormatScalar(value)}");
        }
    }

    private static string DirectoryName(KnowledgeItemType type) => type switch
    {
        KnowledgeItemType.Experience => "experience",
        KnowledgeItemType.Project => "projects",
        KnowledgeItemType.Education => "education",
        KnowledgeItemType.Certification => "certifications",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported knowledge item type."),
    };

    private static string DirectoryNameForKind(EntityKind kind) => kind switch
    {
        EntityKind.Experience => "experience",
        EntityKind.Project => "projects",
        EntityKind.Education => "education",
        EntityKind.Certification => "certifications",
        _ => throw new ArgumentException($"Entity kind '{kind}' does not address a knowledge-base file.", nameof(kind)),
    };
}
