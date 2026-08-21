using Microsoft.Extensions.Logging.Abstractions;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Infrastructure.Knowledge;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Knowledge;

/// <summary>Tests for <see cref="MarkdownKnowledgeBaseReader"/> against synthetic fixture files.</summary>
public sealed class MarkdownKnowledgeBaseReaderTests : IDisposable
{
    private readonly string _root;

    public MarkdownKnowledgeBaseReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rf-kb-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        // Present by default so tests focused on the category directories aren't polluted
        // by the "basics.md not found" warning; tests that care about that path remove it.
        WriteFile("basics.md", "---", "fullName: Test User", "---");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MarkdownKnowledgeBaseReader CreateReader() =>
        new(new StaticProfileRootProvider(_root), NullLogger<MarkdownKnowledgeBaseReader>.Instance);

    private void WriteFile(string relativePath, params string[] lines)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, string.Join('\n', lines) + "\n");
    }

    [Fact]
    public async Task Reads_experience_item_with_all_known_fields()
    {
        WriteFile("experience/acme-corp.md",
            "---",
            "type: experience",
            "role: Senior Software Engineer",
            "organization: Acme Corp",
            "location: Seattle, WA",
            "startDate: 2022-03",
            "endDate: 2024-11",
            "tech: [C#, .NET, PostgreSQL]",
            "tags: [backend, performance]",
            "---",
            "",
            "- Cut p99 latency from 840ms to 120ms.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Type.ShouldBe(KnowledgeItemType.Experience);
        item.Id.ToString().ShouldBe("exp:acme-corp");
        item.Title.ShouldBe("Senior Software Engineer");
        item.Organization.ShouldBe("Acme Corp");
        item.Extra["location"].ShouldBe("Seattle, WA");
        item.StartDate.ShouldBe(new DateOnly(2022, 3, 1));
        item.EndDate.ShouldBe(new DateOnly(2024, 11, 1));
        item.IsCurrent.ShouldBeFalse();
        item.Tech.ShouldBe(["C#", ".NET", "PostgreSQL"]);
        item.Tags.ShouldBe(["backend", "performance"]);
        item.Bullets.ShouldHaveSingleItem().Text.ShouldBe("Cut p99 latency from 840ms to 120ms.");
        item.Source.ShouldBe(KnowledgeSource.Manual);
    }

    [Fact]
    public async Task Reads_project_item_with_source_and_stars_in_extra()
    {
        WriteFile("projects/flowmesh.md",
            "---",
            "type: project",
            "name: Flowmesh",
            "tagline: A distributed task queue",
            "url: https://flowmesh.dev",
            "repoUrl: https://github.com/example/flowmesh",
            "startDate: 2023-01",
            "endDate: present",
            "tech: [Go, Redis]",
            "tags: [infrastructure]",
            "source: github",
            "stars: 1140",
            "---",
            "",
            "- Built a priority-lane scheduler.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Source.ShouldBe(KnowledgeSource.GitHub);
        item.IsCurrent.ShouldBeTrue();
        item.EndDate.ShouldBeNull();
        item.Extra["tagline"].ShouldBe("A distributed task queue");
        item.Extra["url"].ShouldBe("https://flowmesh.dev");
        item.Extra["repoUrl"].ShouldBe("https://github.com/example/flowmesh");
        item.Extra["stars"].ShouldBe("1140");
    }

    [Fact]
    public async Task Parses_indented_bullet_as_a_variant_of_the_preceding_bullet()
    {
        WriteFile("experience/acme-corp.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "---",
            "",
            "- Cut latency from 840ms to 120ms.",
            "  - Rebuilt the fan-out, cutting p99 to 120ms.",
            "- Led a migration project.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets.Count.ShouldBe(2);
        item.Bullets[0].Text.ShouldBe("Cut latency from 840ms to 120ms.");
        item.Bullets[0].Variants.ShouldBe(["Rebuilt the fan-out, cutting p99 to 120ms."]);
        item.Bullets[1].Text.ShouldBe("Led a migration project.");
        item.Bullets[1].Variants.ShouldBeEmpty();
    }

    [Fact]
    public async Task Education_highlights_do_not_support_variants_and_warn_when_nested()
    {
        WriteFile("education/uw.md",
            "---",
            "type: education",
            "institution: University of Washington",
            "credential: B.S. Computer Science",
            "startDate: 2014-09",
            "endDate: 2018-06",
            "---",
            "",
            "- Graduated with honors.",
            "  - This nested item is not a supported variant here.",
            "- Teaching assistant for two years.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets.Count.ShouldBe(2);
        item.Bullets.ShouldAllBe(b => b.Variants.Count == 0);
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Nested"));
    }

    [Fact]
    public async Task Folds_hard_wrapped_continuation_lines_into_the_item_they_belong_to()
    {
        WriteFile("experience/acme-corp.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "---",
            "",
            "- Cut p99 latency from 840ms to 120ms by rebuilding the fan-out path,",
            "  removing three synchronous hops from the request.",
            "  - Rebuilt the fan-out path, cutting p99 latency to 120ms by removing",
            "    three synchronous hops.",
            "- Led a migration project.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets.Count.ShouldBe(2);
        item.Bullets[0].Text.ShouldBe(
            "Cut p99 latency from 840ms to 120ms by rebuilding the fan-out path, removing three synchronous hops from the request.");
        item.Bullets[0].Variants.ShouldBe(
            ["Rebuilt the fan-out path, cutting p99 latency to 120ms by removing three synchronous hops."]);
        item.Bullets[1].Text.ShouldBe("Led a migration project.");
    }

    [Fact]
    public async Task Rejoins_a_word_a_hard_wrap_split_across_a_hyphen()
    {
        WriteFile("experience/acme-corp.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "---",
            "",
            "- Built AutoML workflows and chatbot integrations, cutting model-",
            "  delivery timelines.",
            "- Improved reliability -",
            "  and scalability.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets[0].Text.ShouldBe("Built AutoML workflows and chatbot integrations, cutting model-delivery timelines.");

        // A hyphen the author actually typed as a dash still takes its space: only a hyphen
        // attached to a word on both sides is treated as a split word.
        item.Bullets[1].Text.ShouldBe("Improved reliability - and scalability.");
    }

    [Fact]
    public async Task Folds_hard_wrapped_continuation_lines_into_education_highlights()
    {
        WriteFile("education/uw.md",
            "---",
            "type: education",
            "institution: University of Washington",
            "credential: B.S. Computer Science",
            "endDate: 2018-06",
            "---",
            "",
            "- Coursework: Data Structures & Algorithms, Computational Techniques, Natural Language",
            "  Processing & AI");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        snapshot.Items.ShouldHaveSingleItem().Bullets.ShouldHaveSingleItem().Text.ShouldBe(
            "Coursework: Data Structures & Algorithms, Computational Techniques, Natural Language Processing & AI");
    }

    [Fact]
    public async Task Warns_on_stray_prose_that_does_not_continue_a_list_item()
    {
        WriteFile("experience/acme-corp.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "---",
            "",
            "## Highlights",
            "",
            "- Cut p99 latency to 120ms.",
            "",
            "  This paragraph follows a blank line, so it continues nothing.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets.ShouldHaveSingleItem().Text.ShouldBe("Cut p99 latency to 120ms.");
        snapshot.Diagnostics.Count(d => d.Message.Contains("not a '-' list item")).ShouldBe(2);
    }

    [Fact]
    public async Task Certification_body_is_always_ignored()
    {
        WriteFile("certifications/cka.md",
            "---",
            "type: certification",
            "name: Certified Kubernetes Administrator (CKA)",
            "issuer: CNCF",
            "issuedOn: 2022-11",
            "credentialUrl: https://example.com/cka",
            "---",
            "",
            "- This body content should never become a bullet.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Bullets.ShouldBeEmpty();
        item.StartDate.ShouldBe(new DateOnly(2022, 11, 1));
        item.Extra["credentialUrl"].ShouldBe("https://example.com/cka");
    }

    [Theory]
    [InlineData("2020-05-15")]
    [InlineData("2020-05")]
    [InlineData("2020")]
    public async Task Accepts_every_documented_date_format(string raw)
    {
        WriteFile("certifications/cert-a.md",
            "---",
            "type: certification",
            "name: Some Cert",
            $"issuedOn: {raw}",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        snapshot.Items.ShouldHaveSingleItem().StartDate.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("present")]
    [InlineData("PRESENT")]
    [InlineData("current")]
    public async Task Accepts_present_and_current_as_ongoing(string raw)
    {
        WriteFile("experience/acme.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            $"endDate: {raw}",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.IsCurrent.ShouldBeTrue();
        item.EndDate.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_frontmatter_keys_are_preserved_in_extra_never_an_error()
    {
        WriteFile("experience/acme.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "futureField: some-value",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        snapshot.Items.ShouldHaveSingleItem().Extra["futureField"].ShouldBe("some-value");
    }

    [Fact]
    public async Task Malformed_yaml_produces_an_error_diagnostic_and_is_skipped()
    {
        WriteFile("experience/broken.md",
            "---",
            "type: experience",
            "role: [unterminated",
            "organization: Acme",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Items.ShouldBeEmpty();
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Invalid_date_produces_an_error_diagnostic_and_is_skipped()
    {
        WriteFile("experience/bad-date.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: not-a-date",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Items.ShouldBeEmpty();
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("not-a-date"));
    }

    [Fact]
    public async Task A_single_malformed_file_does_not_abort_the_rest_of_the_load()
    {
        WriteFile("experience/broken.md",
            "---",
            "type: experience",
            "role: [unterminated",
            "---");

        WriteFile("experience/good.md",
            "---",
            "type: experience",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "endDate: 2021-01",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Items.ShouldHaveSingleItem().Slug.ShouldBe("good");
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Missing_required_title_field_is_an_error()
    {
        WriteFile("experience/no-role.md",
            "---",
            "type: experience",
            "organization: Acme",
            "startDate: 2020-01",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Items.ShouldBeEmpty();
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("role"));
    }

    [Fact]
    public async Task Type_mismatch_with_directory_warns_but_still_loads_using_directory_type()
    {
        WriteFile("experience/weird.md",
            "---",
            "type: project",
            "role: Engineer",
            "organization: Acme",
            "startDate: 2020-01",
            "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        var item = snapshot.Items.ShouldHaveSingleItem();
        item.Type.ShouldBe(KnowledgeItemType.Experience);
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Reads_basics_with_summary_body()
    {
        WriteFile("basics.md",
            "---",
            "fullName: Jordan Rivera",
            "headline: Senior Backend Engineer",
            "email: jordan@example.com",
            "---",
            "",
            "Line one of the summary.",
            "Line two of the summary.");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Basics.FullName.ShouldBe("Jordan Rivera");
        snapshot.Basics.Headline.ShouldBe("Senior Backend Engineer");
        snapshot.Basics.Email.ShouldBe("jordan@example.com");
        snapshot.DefaultSummary.ShouldBe("Line one of the summary.\nLine two of the summary.");
    }

    [Fact]
    public async Task Missing_basics_file_produces_a_warning_and_empty_defaults()
    {
        File.Delete(Path.Combine(_root, "basics.md"));

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Basics.FullName.ShouldBe(string.Empty);
        snapshot.DefaultSummary.ShouldBeNull();
        snapshot.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Reads_every_category_directory()
    {
        WriteFile("experience/a.md", "---", "type: experience", "role: R", "organization: O", "startDate: 2020-01", "---");
        WriteFile("projects/b.md", "---", "type: project", "name: N", "startDate: 2020-01", "---");
        WriteFile("education/c.md", "---", "type: education", "institution: I", "credential: C", "---");
        WriteFile("certifications/d.md", "---", "type: certification", "name: CertName", "---");

        var snapshot = await CreateReader().ReadAsync(CancellationToken.None);

        snapshot.Diagnostics.ShouldBeEmpty();
        snapshot.Items.Select(i => i.Type).OrderBy(t => t).ShouldBe(
            new[] { KnowledgeItemType.Experience, KnowledgeItemType.Project, KnowledgeItemType.Education, KnowledgeItemType.Certification }.OrderBy(t => t));
    }
}
