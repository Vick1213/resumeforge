using System.Text;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Rendering;

/// <summary>
/// Composite <see cref="IResumeRenderer"/> that dispatches to a format-specific renderer.
/// <see cref="RenderFormat.Docx"/> is not implemented and throws
/// <see cref="NotSupportedException"/>, which the API maps to HTTP 501.
/// </summary>
public sealed class ResumeRenderer(
    MarkdownResumeRenderer markdownRenderer,
    HtmlResumeRenderer htmlRenderer,
    PdfResumeRenderer pdfRenderer) : IResumeRenderer
{
    /// <inheritdoc />
    public Task<RenderedDocument> RenderAsync(ResumeDocument doc, RenderFormat format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();

        var baseName = FileBaseName(doc.Basics.FullName);

        return format switch
        {
            RenderFormat.Markdown => Task.FromResult(new RenderedDocument
            {
                Content = Encoding.UTF8.GetBytes(markdownRenderer.Render(doc)),
                ContentType = "text/markdown; charset=utf-8",
                FileName = $"{baseName}.md",
            }),
            RenderFormat.Html => Task.FromResult(new RenderedDocument
            {
                Content = Encoding.UTF8.GetBytes(htmlRenderer.Render(doc)),
                ContentType = "text/html; charset=utf-8",
                FileName = $"{baseName}.html",
            }),
            RenderFormat.Pdf => Task.FromResult(RenderPdf(doc, baseName)),
            RenderFormat.Docx => throw new NotSupportedException("DOCX rendering is not supported in this version."),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown render format."),
        };
    }

    private RenderedDocument RenderPdf(ResumeDocument doc, string baseName)
    {
        var rendered = pdfRenderer.Render(doc);

        return new RenderedDocument
        {
            Content = rendered.Content,
            ContentType = "application/pdf",
            FileName = $"{baseName}.pdf",
            PageCount = rendered.PageCount,
        };
    }

    private static string FileBaseName(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "resume" : $"{Slug.From(fullName)}-resume";
}
