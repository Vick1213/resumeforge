using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Knowledge;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Knowledge;

/// <summary>Granular tests for <see cref="MarkdownKnowledgeBaseWriter"/> beyond pure round-trip.</summary>
public sealed class MarkdownKnowledgeBaseWriterTests : IDisposable
{
    private readonly string _root;
    private readonly MarkdownKnowledgeBaseWriter _writer;

    public MarkdownKnowledgeBaseWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rf-kb-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _writer = new MarkdownKnowledgeBaseWriter(new StaticProfileRootProvider(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_omits_missing_optional_fields()
    {
        var item = TestData.KnowledgeItem(
            Domain.Knowledge.KnowledgeItemType.Project, "tiny-orm", "TinyOrm",
            start: new DateOnly(2020, 8, 1), end: new DateOnly(2021, 3, 1));

        await _writer.WriteAsync(item, CancellationToken.None);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, "projects", "tiny-orm.md"), TestContext.Current.CancellationToken);

        text.ShouldNotContain("tagline:");
        text.ShouldNotContain("url:");
        text.ShouldNotContain("stars:");
        text.ShouldContain("source: manual");
    }

    [Fact]
    public async Task Write_creates_the_category_directory_when_missing()
    {
        var item = TestData.KnowledgeItem(Domain.Knowledge.KnowledgeItemType.Education, "some-school", "B.S. Foo", organization: "Some School");

        Directory.Exists(Path.Combine(_root, "education")).ShouldBeFalse();

        await _writer.WriteAsync(item, CancellationToken.None);

        File.Exists(Path.Combine(_root, "education", "some-school.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_removes_an_existing_file()
    {
        var item = TestData.KnowledgeItem(Domain.Knowledge.KnowledgeItemType.Certification, "cka", "CKA");
        await _writer.WriteAsync(item, CancellationToken.None);

        var path = Path.Combine(_root, "certifications", "cka.md");
        File.Exists(path).ShouldBeTrue();

        await _writer.DeleteAsync(EntityId.Parse("cert:cka"), CancellationToken.None);

        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_of_a_nonexistent_file_does_not_throw()
    {
        await Should.NotThrowAsync(() => _writer.DeleteAsync(EntityId.Parse("cert:ghost"), CancellationToken.None));
    }

    [Fact]
    public async Task WriteBasics_without_a_summary_omits_the_body()
    {
        var basics = new ResumeBasics { FullName = "Taylor Doe" };

        await _writer.WriteBasicsAsync(basics, summary: null, CancellationToken.None);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, "basics.md"), TestContext.Current.CancellationToken);
        text.ShouldBe("---\nfullName: Taylor Doe\n---\n");
    }

    [Fact]
    public async Task Write_appends_genuinely_unknown_extra_keys_after_known_fields_sorted()
    {
        var item = TestData.KnowledgeItem(
            Domain.Knowledge.KnowledgeItemType.Certification, "az-204", "AZ-204", organization: "Microsoft",
            extra: new Dictionary<string, string> { ["zetaField"] = "z", ["alphaField"] = "a" });

        await _writer.WriteAsync(item, CancellationToken.None);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, "certifications", "az-204.md"), TestContext.Current.CancellationToken);
        var alphaIndex = text.IndexOf("alphaField:", StringComparison.Ordinal);
        var zetaIndex = text.IndexOf("zetaField:", StringComparison.Ordinal);

        alphaIndex.ShouldBeGreaterThan(0);
        zetaIndex.ShouldBeGreaterThan(alphaIndex);
    }
}
