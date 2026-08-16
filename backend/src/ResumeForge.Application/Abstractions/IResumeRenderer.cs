using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port that renders a canonical <see cref="ResumeDocument"/> to a downloadable file.
/// Implemented in Infrastructure against format-specific templates.
/// </summary>
public interface IResumeRenderer
{
    /// <summary>Renders <paramref name="doc"/> to <paramref name="format"/>.</summary>
    Task<RenderedDocument> RenderAsync(ResumeDocument doc, RenderFormat format, CancellationToken ct);
}

/// <summary>The output formats a resume can be rendered to.</summary>
public enum RenderFormat
{
    /// <summary>Portable document format.</summary>
    Pdf,

    /// <summary>Standalone HTML.</summary>
    Html,

    /// <summary>Plain markdown.</summary>
    Markdown,

    /// <summary>Microsoft Word document.</summary>
    Docx,
}

/// <summary>The rendered bytes for a resume, ready to return as a file download.</summary>
public sealed record RenderedDocument
{
    /// <summary>The rendered file content.</summary>
    public required byte[] Content { get; init; }

    /// <summary>The MIME type of <see cref="Content"/>.</summary>
    public required string ContentType { get; init; }

    /// <summary>The suggested download file name.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The number of rendered pages, for formats where pagination is meaningful
    /// (<see cref="RenderFormat.Pdf"/>). Null for formats with no fixed page size
    /// (<see cref="RenderFormat.Html"/>, <see cref="RenderFormat.Markdown"/>).
    /// </summary>
    public int? PageCount { get; init; }
}
