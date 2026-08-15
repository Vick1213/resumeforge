using System.Diagnostics.CodeAnalysis;
using ResumeForge.Domain.Ids;

namespace ResumeForge.Domain.Resume;

/// <summary>
/// The canonical, in-memory representation of a resume: basics, sections, and every
/// addressable node the tailoring engine can act on.
/// </summary>
public sealed record ResumeDocument
{
    /// <summary>Unique identifier for this resume (a guid string).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name, e.g. "Base resume" or "Acme - Backend Eng".</summary>
    public required string Name { get; init; }

    /// <summary>Contact and identity information.</summary>
    public required ResumeBasics Basics { get; init; }

    /// <summary>The summary block text, addressed by the id <c>"sum"</c>.</summary>
    public string? Summary { get; init; }

    /// <summary>Skill groups, in display order.</summary>
    public required IReadOnlyList<SkillGroup> Skills { get; init; }

    /// <summary>Experience entries, in display order.</summary>
    public required IReadOnlyList<ExperienceEntry> Experience { get; init; }

    /// <summary>Project entries, in display order.</summary>
    public required IReadOnlyList<ProjectEntry> Projects { get; init; }

    /// <summary>Education entries, in display order.</summary>
    public required IReadOnlyList<EducationEntry> Education { get; init; }

    /// <summary>Certification entries, in display order.</summary>
    public required IReadOnlyList<CertificationEntry> Certifications { get; init; }

    /// <summary>The order sections are rendered in.</summary>
    public required IReadOnlyList<SectionKind> SectionOrder { get; init; }

    /// <summary>When this resume was first created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this resume was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Attempts to locate the bullet addressed by <paramref name="id"/> among all
    /// experience and project entries.
    /// </summary>
    public bool TryFindBullet(EntityId id, [MaybeNullWhen(false)] out Bullet bullet)
    {
        foreach (var (bulletId, candidate) in EnumerateBulletRecords())
        {
            if (bulletId == id)
            {
                bullet = candidate;
                return true;
            }
        }

        bullet = default;
        return false;
    }

    /// <summary>Enumerates every bullet in the document as its id and current text.</summary>
    public IEnumerable<(EntityId Id, string Text)> EnumerateBullets()
    {
        foreach (var (id, bullet) in EnumerateBulletRecords())
        {
            yield return (id, bullet.Text);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="id"/> resolves to any addressable node in this
    /// document: an entry, a bullet, a skill group, a skill, or the summary block.
    /// </summary>
    public bool ContainsNode(EntityId id)
    {
        if (id.Kind == EntityKind.Summary)
        {
            return true;
        }

        var hasFragment = id.Ordinal is not null || id.SubKey is not null;
        var idText = id.ToString();

        return id.Kind switch
        {
            EntityKind.Experience => hasFragment
                ? TryFindBullet(id, out _)
                : Experience.Any(e => e.Id == idText),
            EntityKind.Project => hasFragment
                ? TryFindBullet(id, out _)
                : Projects.Any(p => p.Id == idText),
            EntityKind.Education => !hasFragment && Education.Any(e => e.Id == idText),
            EntityKind.Certification => !hasFragment && Certifications.Any(c => c.Id == idText),
            EntityKind.SkillGroup => hasFragment
                ? Skills.Any(g => g.Id == id.Parent.ToString() && g.Items.Any(s => s.Id == idText))
                : Skills.Any(g => g.Id == idText),
            _ => false,
        };
    }

    private IEnumerable<(EntityId Id, Bullet Bullet)> EnumerateBulletRecords()
    {
        foreach (var entry in Experience)
        {
            foreach (var bullet in entry.Bullets)
            {
                if (EntityId.TryParse(bullet.Id, out var id))
                {
                    yield return (id, bullet);
                }
            }
        }

        foreach (var entry in Projects)
        {
            foreach (var bullet in entry.Bullets)
            {
                if (EntityId.TryParse(bullet.Id, out var id))
                {
                    yield return (id, bullet);
                }
            }
        }
    }
}
