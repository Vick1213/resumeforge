using System.Text.Json;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Skills;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="HeuristicLanguageModel"/>: deterministic, no-network, zero-rewrite
/// command proposals whose output always passes the real <see cref="CommandValidator"/>.
/// </summary>
public sealed class HeuristicLanguageModelTests
{
    private readonly TailorOptions _options = new();
    private readonly HeuristicLanguageModel _model;

    public HeuristicLanguageModelTests()
    {
        _model = new HeuristicLanguageModel(_options);
    }

    [Fact]
    public void ModelId_is_stable()
    {
        _model.ModelId.ShouldBe("heuristic-v1");
    }

    [Fact]
    public async Task Reports_zero_token_usage_zero_model_calls_and_is_never_from_cache()
    {
        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest("REQUIREMENTS\n"), CancellationToken.None);

        response.Usage.ShouldBe(TokenUsage.Empty);
        response.Usage.ModelCalls.ShouldBe(0);
        response.FromCache.ShouldBeFalse();
    }

    [Fact]
    public async Task Never_emits_a_rewrite_command()
    {
        const string brief = "CANDIDATES-EXPERIENCE\nexp:acme#0|v1|Some bullet text about the role.\nexp:acme#1|v0|Another bullet.\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is RewriteCommand);
    }

    [Fact]
    public async Task Is_deterministic_for_the_same_brief()
    {
        const string brief = "CANDIDATES-EXPERIENCE\nexp:acme#0|v1|Text one.\nexp:acme#1|v0|Text two.\nCANDIDATES-SKILLS\nskl:languages#csharp|C#\n";

        var first = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);
        var second = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        JsonSerializer.Serialize(first.Value).ShouldBe(JsonSerializer.Serialize(second.Value));
    }

    [Fact]
    public async Task Throws_for_an_unsupported_schema_name()
    {
        var request = new ModelRequest { System = "s", User = "u", SchemaName = "field-resolutions" };

        await Should.ThrowAsync<NotSupportedException>(() => _model.CompleteAsync<object>(request, CancellationToken.None));
    }

    [Fact]
    public async Task Selects_variant_zero_when_a_candidate_bullet_has_variants()
    {
        const string brief = "CANDIDATES-EXPERIENCE\nexp:acme#0|v2|Some bullet text.\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.OfType<SelectVariantCommand>().ShouldContain(c => c.Target == "exp:acme#0" && c.VariantIndex == 0);
    }

    [Fact]
    public async Task Does_not_select_a_variant_when_none_is_available()
    {
        const string brief = "CANDIDATES-EXPERIENCE\nexp:acme#0|v0|Some bullet text.\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is SelectVariantCommand);
    }

    [Fact]
    public async Task Emphasizes_the_normalized_skills_from_the_skill_candidates_section()
    {
        const string brief = "CANDIDATES-SKILLS\nskl:languages#csharp|C#\nskl:cloud#kubernetes|Kubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var emphasize = response.Value.OfType<EmphasizeSkillsCommand>().ShouldHaveSingleItem();
        emphasize.Skills.ShouldBe(["csharp", "kubernetes"]);
    }

    [Fact]
    public async Task Emits_nothing_for_an_empty_brief()
    {
        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest("REQUIREMENTS\n"), CancellationToken.None);

        response.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Excludes_entries_beyond_the_configured_experience_cap()
    {
        var model = new HeuristicLanguageModel(new TailorOptions { MaxExperienceEntries = 1 });
        const string brief = "CANDIDATES-EXPERIENCE\nexp:a#0|v0|text a\nexp:b#0|v0|text b\n";

        var response = await model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.OfType<IncludeCommand>().Single().Targets.ShouldBe(["exp:a"]);
        response.Value.OfType<ExcludeCommand>().Single().Targets.ShouldBe(["exp:b"]);
    }

    [Fact]
    public async Task Orders_bullets_within_a_kept_entry_by_brief_order()
    {
        const string brief = "CANDIDATES-EXPERIENCE\nexp:acme#2|v0|third\nexp:acme#0|v0|first\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var order = response.Value.OfType<OrderCommand>().Single(o => o.Parent == "exp:acme");
        order.Order.ShouldBe(["exp:acme#2", "exp:acme#0"]);
    }

    [Fact]
    public async Task Full_pipeline_output_passes_the_real_command_validator()
    {
        var taxonomy = new SkillTaxonomy();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var snapshot = new KnowledgeBaseSnapshot
        {
            Items =
            [
                TestData.KnowledgeItem(
                    KnowledgeItemType.Experience, "acme", "Senior Backend Engineer", organization: "Acme",
                    start: new DateOnly(2022, 1, 1), isCurrent: true,
                    tech: ["C#", "Kubernetes", ".NET"],
                    bullets:
                    [
                        TestData.KnowledgeBullet(
                            "Cut checkout latency from 800ms to 100ms by rebuilding the fan-out.",
                            variants: ["Rebuilt the checkout fan-out, cutting latency 8x."]),
                        TestData.KnowledgeBullet("Led a migration of 20 services to .NET 8."),
                    ]),
                TestData.KnowledgeItem(
                    KnowledgeItemType.Project, "widget-tool", "Widget Tool",
                    start: new DateOnly(2021, 1, 1), end: new DateOnly(2022, 1, 1),
                    tech: ["TypeScript"],
                    bullets: [TestData.KnowledgeBullet("Built a CLI tool used by 200 developers.")]),
            ],
            Basics = TestData.Basics("Jordan Rivera"),
            DefaultSummary = "A backend engineer.",
            Diagnostics = [],
        };

        var baseResume = new ResumeForge.Application.Tailoring.ResumeBuilder(taxonomy, time).Build(snapshot);

        var posting = TestData.Posting(rawText:
            "We are hiring a Senior Backend Engineer. Required: 5+ years of experience with C# and .NET. " +
            "Required: hands-on experience with Kubernetes. Nice to have: TypeScript.");
        var analysis = new JobAnalyzer(taxonomy).Analyze(posting);

        var candidates = new Bm25RelevanceScorer(time, new ScoringOptions()).Score(baseResume, analysis);
        var brief = new BriefBuilder().Build(analysis, candidates, baseResume, _options);

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var validation = new CommandValidator(new FabricationGuard()).Validate(response.Value, baseResume, _options);

        validation.Rejected.ShouldBeEmpty();
        response.Value.ShouldNotContain(c => c is RewriteCommand);
    }

    private static ModelRequest NewRequest(string brief) => new()
    {
        System = "You propose tailoring commands.",
        User = brief,
        SchemaName = JsonSchemaRegistry.TailorCommandsSchemaName,
    };
}
