using Microsoft.Extensions.Logging.Abstractions;
using ResumeForge.Infrastructure.Knowledge;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Knowledge;

/// <summary>
/// Hard requirement from CONTRACTS.md §3: reading a knowledge-base file and writing it
/// straight back must be byte-identical. Exercised against the repository's real
/// <c>profile/</c> directory, located relative to the test assembly (never a hardcoded
/// path) so this passes on any machine, including CI.
/// </summary>
public sealed class ProfileRoundTripTests : IDisposable
{
    private readonly string _outputRoot;

    public ProfileRoundTripTests()
    {
        _outputRoot = Path.Combine(Path.GetTempPath(), "rf-kb-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
    }

    public static IEnumerable<object[]> RealProfileFiles()
    {
        foreach (var category in new[] { "experience", "projects", "education", "certifications" })
        {
            var dir = Path.Combine(RepoPaths.ProfileDirectory, category);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return [Path.GetRelativePath(RepoPaths.ProfileDirectory, file).Replace(Path.DirectorySeparatorChar, '/')];
            }
        }
    }

    [Fact]
    public async Task Loading_the_real_profile_directory_produces_no_diagnostics()
    {
        var reader = new MarkdownKnowledgeBaseReader(
            new StaticProfileRootProvider(RepoPaths.ProfileDirectory), NullLogger<MarkdownKnowledgeBaseReader>.Instance);

        var snapshot = await reader.ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        snapshot.Items.ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(RealProfileFiles))]
    public async Task Reading_then_writing_a_real_profile_file_is_byte_identical(string relativePath)
    {
        var reader = new MarkdownKnowledgeBaseReader(
            new StaticProfileRootProvider(RepoPaths.ProfileDirectory), NullLogger<MarkdownKnowledgeBaseReader>.Instance);
        var snapshot = await reader.ReadAsync(CancellationToken.None);

        var item = snapshot.Items.Single(i => i.FilePath == relativePath);

        var writer = new MarkdownKnowledgeBaseWriter(new StaticProfileRootProvider(_outputRoot));
        await writer.WriteAsync(item, CancellationToken.None);

        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(RepoPaths.ProfileDirectory, relativePath), TestContext.Current.CancellationToken);
        var writtenBytes = await File.ReadAllBytesAsync(Path.Combine(_outputRoot, relativePath), TestContext.Current.CancellationToken);

        writtenBytes.ShouldBe(originalBytes);
    }

    [Fact]
    public async Task Reading_then_writing_basics_md_is_byte_identical()
    {
        var reader = new MarkdownKnowledgeBaseReader(
            new StaticProfileRootProvider(RepoPaths.ProfileDirectory), NullLogger<MarkdownKnowledgeBaseReader>.Instance);
        var snapshot = await reader.ReadAsync(CancellationToken.None);

        var writer = new MarkdownKnowledgeBaseWriter(new StaticProfileRootProvider(_outputRoot));
        await writer.WriteBasicsAsync(snapshot.Basics, snapshot.DefaultSummary, CancellationToken.None);

        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(RepoPaths.ProfileDirectory, "basics.md"), TestContext.Current.CancellationToken);
        var writtenBytes = await File.ReadAllBytesAsync(Path.Combine(_outputRoot, "basics.md"), TestContext.Current.CancellationToken);

        writtenBytes.ShouldBe(originalBytes);
    }
}
