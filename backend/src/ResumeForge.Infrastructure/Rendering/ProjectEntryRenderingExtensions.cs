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
}
