using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Rendering;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Rendering;

/// <summary>Tests for the <see cref="ResumeRenderer"/> dispatcher.</summary>
public sealed class ResumeRendererTests
{
    private readonly ResumeRenderer _renderer = new(new MarkdownResumeRenderer(), new HtmlResumeRenderer(), new PdfResumeRenderer());

    [Theory]
    [InlineData(RenderFormat.Markdown, "text/markdown; charset=utf-8", ".md")]
    [InlineData(RenderFormat.Html, "text/html; charset=utf-8", ".html")]
    [InlineData(RenderFormat.Pdf, "application/pdf", ".pdf")]
    public async Task Dispatches_to_the_correct_format_with_matching_content_type_and_extension(RenderFormat format, string contentType, string extension)
    {
        var result = await _renderer.RenderAsync(RenderingTestData.Document(), format, CancellationToken.None);

        result.ContentType.ShouldBe(contentType);
        result.FileName.ShouldEndWith(extension);
        result.Content.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Docx_throws_NotSupportedException()
    {
        await Should.ThrowAsync<NotSupportedException>(
            () => _renderer.RenderAsync(RenderingTestData.Document(), RenderFormat.Docx, CancellationToken.None));
    }

    [Fact]
    public async Task File_name_is_derived_from_the_candidates_name()
    {
        var result = await _renderer.RenderAsync(RenderingTestData.Document(), RenderFormat.Markdown, CancellationToken.None);

        result.FileName.ShouldBe("jordan-rivera-resume.md");
    }
}
