using System.Diagnostics.CodeAnalysis;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Rendering;

/// <summary>
/// Presentation-only predicate shared by the PDF, HTML, and Markdown renderers so a project
/// entry with nothing to show is omitted consistently across formats.
/// </summary>
internal static class ProjectEntryRenderingExtensions
{
    /// <summary>
    /// True when <paramref name="entry"/> has content beyond its name and date range — at
    /// least one bullet or a non-blank tagline. A project with neither renders as a bare
    /// name-and-dates line, which looks broken, so renderers skip it. The entry itself is
    /// untouched; this only decides whether it appears in rendered output.
    /// </summary>
    public static bool HasRenderableContent(this ProjectEntry entry) =>
        entry.Bullets.Count > 0 || !string.IsNullOrWhiteSpace(entry.Tagline);

    /// <summary>
    /// True when <paramref name="entry"/>'s tagline should be rendered: it is non-blank
    /// <em>and</em> the entry has no bullets of its own.
    /// </summary>
    /// <remarks>
    /// A tagline restates, in a full-width sentence, what the bullets under it are about to
    /// evidence in detail — so on a project that has bullets it buys nothing and costs one to
    /// two lines, five times over on a projects-heavy resume. That is the single largest
    /// block of low-value space on the page, and the reference formats this layout follows
    /// carry no such line at all. Where a project has <em>no</em> bullets the tagline is the
    /// only thing it can say, so it still renders; <see cref="HasRenderableContent"/> is what
    /// keeps an entry with neither off the page entirely.
    /// </remarks>
    public static bool ShouldRenderTagline(this ProjectEntry entry, [NotNullWhen(true)] out string? tagline)
    {
        tagline = entry.Bullets.Count == 0 && !string.IsNullOrWhiteSpace(entry.Tagline) ? entry.Tagline : null;
        return tagline is not null;
    }
}
