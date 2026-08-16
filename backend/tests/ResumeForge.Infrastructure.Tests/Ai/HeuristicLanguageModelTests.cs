using System.Text.Json;
using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Skills;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="HeuristicLanguageModel"/>: deterministic, no-network, zero-rewrite
/// command proposals whose output always passes the real <see cref="CommandValidator"/>, and
/// the no-network field-resolution path <c>POST /api/autofill/resolve</c> relies on when no
/// API key is configured.
/// </summary>
public sealed class HeuristicLanguageModelTests
{
    private readonly TailorOptions _options = new();
    private readonly IKnowledgeBaseReader _knowledgeBaseReader = Substitute.For<IKnowledgeBaseReader>();
    private readonly ISkillTaxonomy _taxonomy = new SkillTaxonomy();
    private readonly HeuristicLanguageModel _model;

    public HeuristicLanguageModelTests()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(EmptySnapshot());
        _model = new HeuristicLanguageModel(_options, _knowledgeBaseReader, _taxonomy);
    }

    private static KnowledgeBaseSnapshot EmptySnapshot(ResumeBasics? basics = null) => new()
    {
        Items = [],
        Basics = basics ?? new ResumeBasics { FullName = "Jordan Rivera" },
        Diagnostics = [],
    };

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
        var request = new ModelRequest { System = "s", User = "u", SchemaName = "some-other-schema" };

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
        var model = new HeuristicLanguageModel(new TailorOptions { MaxExperienceEntries = 1 }, _knowledgeBaseReader, _taxonomy);
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

    [Fact]
    public async Task Emits_no_keyword_injection_when_the_bullet_itself_gives_no_specific_evidence_for_it()
    {
        // The entry's tech: list evidences "kubernetes" for the run overall, but this bullet
        // never mentions Kubernetes or any known abbreviation of it — entry-level evidence
        // alone is exactly the gap that used to let a keyword land on an unrelated bullet, so
        // it must not be enough on its own.
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items =
            [
                TestData.KnowledgeItem(
                    KnowledgeItemType.Experience, "acme", "Engineer", tech: ["Kubernetes"],
                    bullets: [TestData.KnowledgeBullet("Led a platform migration effort.")]),
            ],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief =
            "EFFORT|thorough\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Led a platform migration effort.\nKEYWORDS\nkubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is InjectKeywordsCommand);
    }

    [Fact]
    public async Task Emits_a_natural_keyword_injection_when_the_bullets_own_text_spells_out_an_abbreviation()
    {
        // The bullet already talks about Kubernetes, just abbreviated as "K8s" — real,
        // bullet-specific evidence the entry's tech: list alone can't provide. This is the
        // one shape of injection the heuristic can produce honestly: spelling the taxonomy's
        // display name out in place, never an appended sentence.
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items =
            [
                TestData.KnowledgeItem(
                    KnowledgeItemType.Experience, "acme", "Engineer", tech: ["Kubernetes"],
                    bullets: [TestData.KnowledgeBullet("Operated services on K8s clusters at scale.")]),
            ],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief =
            "EFFORT|thorough\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Operated services on K8s clusters at scale.\nKEYWORDS\nkubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var inject = response.Value.OfType<InjectKeywordsCommand>().ShouldHaveSingleItem();
        inject.Target.ShouldBe("exp:acme#0");
        inject.Keywords.ShouldBe(["kubernetes"]);
        inject.Text.ShouldBe("Operated services on K8s (Kubernetes) clusters at scale.");

        // The display name, never the normalized canonical, appears in user-visible prose
        // (case-sensitive: "Kubernetes" must be present, all-lowercase "kubernetes" must not).
        inject.Text.Contains("Kubernetes", StringComparison.Ordinal).ShouldBeTrue();
        inject.Text.Contains("kubernetes", StringComparison.Ordinal).ShouldBeFalse();

        // No canned suffix, ever — the fixed shape this whole change exists to remove.
        inject.Text.ShouldNotContain("Also applied");
        inject.Text.ShouldNotBe("Operated services on K8s clusters at scale. Also applied kubernetes in this work.");
    }

    [Fact]
    public async Task Never_injects_a_keyword_into_a_bullet_its_entry_does_not_evidence_it_for()
    {
        // Reproduces the reported defect directly: a Kafka mention landing on a sentence
        // about mentoring engineers, which has nothing to do with Kafka. The entry's tech:
        // list evidences "kafka" for the run overall (and a sibling bullet even states it
        // plainly), but the mentoring bullet itself gives zero evidence, so it must never
        // receive the keyword regardless of what else in the same entry does.
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items =
            [
                TestData.KnowledgeItem(
                    KnowledgeItemType.Experience, "nimbus-systems", "Senior Software Engineer", organization: "Nimbus Systems",
                    tech: ["C#", ".NET", "PostgreSQL", "Kubernetes", "Kafka"],
                    bullets:
                    [
                        TestData.KnowledgeBullet(
                            "Led the migration of 40+ services from .NET 6 to .NET 8, retiring 12,000 lines of " +
                            "compatibility shim code across the platform."),
                        TestData.KnowledgeBullet(
                            "Mentored 3 mid-level engineers through promotion to senior, two of whom now lead " +
                            "their own service teams."),
                        TestData.KnowledgeBullet(
                            "Cut p99 checkout latency from 840ms to 120ms by replacing a serial fan-out with a " +
                            "bounded parallel scatter-gather over 6 downstream services."),
                        TestData.KnowledgeBullet("Designed and shipped an event-sourced order pipeline on Kafka."),
                    ]),
            ],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief =
            "EFFORT|thorough\n" +
            "CANDIDATES-EXPERIENCE\n" +
            "exp:nimbus-systems#0|v0|Led the migration of 40+ services from .NET 6 to .NET 8.\n" +
            "exp:nimbus-systems#1|v0|Mentored 3 mid-level engineers through promotion to senior.\n" +
            "exp:nimbus-systems#2|v0|Cut p99 checkout latency from 840ms to 120ms.\n" +
            "exp:nimbus-systems#3|v0|Designed and shipped an event-sourced order pipeline on Kafka.\n" +
            "KEYWORDS\ncsharp\nkafka\nkubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var injections = response.Value.OfType<InjectKeywordsCommand>().ToList();

        // The exact reported failure: nothing ever targets the mentoring bullet.
        injections.ShouldNotContain(c => c.Target == "exp:nimbus-systems#1");

        // None of these bullets spell any of the three keywords out under a different
        // abbreviation, so — honestly — nothing qualifies for any of them.
        injections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Never_leaks_a_normalized_canonical_form_into_injected_prose()
    {
        // "cs" is a real taxonomy alias for C#; the bullet uses it as a standalone word, so
        // this is genuine bullet-level evidence — the injected prose must read "C#", the
        // taxonomy's display spelling, and must never contain the raw normalized key
        // "csharp" the model reasons about internally.
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items =
            [
                TestData.KnowledgeItem(
                    KnowledgeItemType.Experience, "acme", "Engineer", tech: ["C#"],
                    bullets: [TestData.KnowledgeBullet("Owns the internal cs tooling library used company-wide.")]),
            ],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief =
            "EFFORT|thorough\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Owns the internal cs tooling library used company-wide.\nKEYWORDS\ncsharp\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var inject = response.Value.OfType<InjectKeywordsCommand>().ShouldHaveSingleItem();
        inject.Text.Contains("C#", StringComparison.Ordinal).ShouldBeTrue();
        inject.Text.Contains("csharp", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Does_not_emit_keyword_injections_below_thorough_effort_even_with_a_keywords_section()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [TestData.KnowledgeItem(KnowledgeItemType.Experience, "acme", "Engineer", tech: ["Kubernetes"])],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief =
            "EFFORT|standard\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Led a platform migration effort.\nKEYWORDS\nkubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is InjectKeywordsCommand);
    }

    [Fact]
    public async Task Skips_a_keyword_not_evidenced_anywhere_in_the_knowledge_base()
    {
        // No effort was omitted for this test: the brief itself defaults to Standard when
        // no EFFORT| line is present, so this also pins that an absent EFFORT| line never
        // enables injectKeywords — the same "omitted means Standard" rule the wire contract
        // uses (CONTRACTS.md §9).
        const string brief = "EFFORT|thorough\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Led a platform migration effort.\nKEYWORDS\nkubernetes\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is InjectKeywordsCommand);
    }

    [Fact]
    public async Task Skips_a_digit_bearing_keyword_rather_than_risk_a_fabricated_number()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [TestData.KnowledgeItem(KnowledgeItemType.Experience, "acme", "Engineer", tech: ["Python3"])],
            Basics = TestData.Basics(),
            Diagnostics = [],
        });

        const string brief = "EFFORT|thorough\nCANDIDATES-EXPERIENCE\nexp:acme#0|v0|Led a platform migration effort.\nKEYWORDS\npython3\n";

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is InjectKeywordsCommand);
    }

    [Fact]
    public async Task Full_pipeline_at_thorough_effort_emits_a_validator_accepted_natural_keyword_injection()
    {
        var taxonomy = new SkillTaxonomy();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = new TailorOptions { Effort = ModelEffort.Thorough };

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
                        TestData.KnowledgeBullet("Migrated checkout services to run on K8s, cutting latency from 800ms to 100ms."),
                        TestData.KnowledgeBullet("Led a migration of 20 services to .NET 8."),
                    ]),
            ],
            Basics = TestData.Basics("Jordan Rivera"),
            DefaultSummary = "A backend engineer.",
            Diagnostics = [],
        };

        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        var baseResume = new ResumeForge.Application.Tailoring.ResumeBuilder(taxonomy, time).Build(snapshot);

        var posting = TestData.Posting(rawText: "We are hiring a Senior Backend Engineer. Required: hands-on experience with Kubernetes.");
        var analysis = new JobAnalyzer(taxonomy).Analyze(posting);

        var candidates = new Bm25RelevanceScorer(time, new ScoringOptions()).Score(baseResume, analysis);
        var brief = new BriefBuilder().Build(analysis, candidates, baseResume, options);

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        var inject = response.Value.OfType<InjectKeywordsCommand>().ShouldHaveSingleItem();
        inject.Keywords.ShouldContain("kubernetes");
        inject.Text.Contains("Kubernetes", StringComparison.Ordinal).ShouldBeTrue();
        inject.Text.Contains("kubernetes", StringComparison.Ordinal).ShouldBeFalse();
        inject.Text.ShouldNotContain("Also applied");

        var validation = new CommandValidator(new FabricationGuard()).Validate(response.Value, baseResume, options);
        validation.Rejected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Full_pipeline_at_thorough_effort_emits_nothing_when_no_bullet_gives_specific_evidence()
    {
        // Pins the honest outcome for a knowledge base like this one: the entry claims
        // Kubernetes overall, but neither bullet's own text names it under any spelling, so
        // Thorough must produce exactly what Standard would — no injectKeywords at all,
        // rather than forcing the keyword onto whichever bullet happens to be available. A
        // future change that starts emitting filler again to "use" the keyword must fail
        // this test.
        var taxonomy = new SkillTaxonomy();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = new TailorOptions { Effort = ModelEffort.Thorough };

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
                        TestData.KnowledgeBullet("Cut checkout latency from 800ms to 100ms by rebuilding the fan-out."),
                        TestData.KnowledgeBullet("Led a migration of 20 services to .NET 8."),
                    ]),
            ],
            Basics = TestData.Basics("Jordan Rivera"),
            DefaultSummary = "A backend engineer.",
            Diagnostics = [],
        };

        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        var baseResume = new ResumeForge.Application.Tailoring.ResumeBuilder(taxonomy, time).Build(snapshot);

        var posting = TestData.Posting(rawText: "We are hiring a Senior Backend Engineer. Required: hands-on experience with Kubernetes.");
        var analysis = new JobAnalyzer(taxonomy).Analyze(posting);

        var candidates = new Bm25RelevanceScorer(time, new ScoringOptions()).Score(baseResume, analysis);
        var brief = new BriefBuilder().Build(analysis, candidates, baseResume, options);

        var response = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(brief), CancellationToken.None);

        response.Value.ShouldNotContain(c => c is InjectKeywordsCommand);

        var standardBrief = new BriefBuilder().Build(analysis, candidates, baseResume, new TailorOptions { Effort = ModelEffort.Standard });
        var standardResponse = await _model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewRequest(standardBrief), CancellationToken.None);

        JsonSerializer.Serialize(response.Value).ShouldBe(JsonSerializer.Serialize(standardResponse.Value));
    }

    [Fact]
    public async Task Resolve_fields_reports_zero_token_usage_and_is_never_from_cache()
    {
        var brief = FieldResolutionBrief([("el-1", "email", "Email Address", null, null, null)]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        response.Usage.ShouldBe(TokenUsage.Empty);
        response.FromCache.ShouldBeFalse();
    }

    [Fact]
    public async Task Resolve_fields_returns_nothing_for_an_empty_fields_section()
    {
        var brief = FieldResolutionBrief([]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        response.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolve_fields_matches_a_field_by_label_against_the_synonym_table()
    {
        var brief = FieldResolutionBrief([("el-1", "email", "Email Address", null, null, null)]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        var resolution = response.Value.ShouldHaveSingleItem();
        resolution.ElementId.ShouldBe("el-1");
        resolution.CanonicalKey.ShouldBe("email");
        resolution.Confidence.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Resolve_fields_short_circuits_on_an_exact_autocomplete_token_match()
    {
        var brief = FieldResolutionBrief([("el-1", "text", "Some odd label with no signal", null, null, "email")]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        var resolution = response.Value.ShouldHaveSingleItem();
        resolution.CanonicalKey.ShouldBe("email");
        resolution.Confidence.ShouldBe(1.0);
    }

    [Fact]
    public async Task Resolve_fields_emits_an_empty_canonical_key_for_a_genuinely_unmappable_field()
    {
        var brief = FieldResolutionBrief([("el-1", "text", "Favorite pizza topping", null, null, null)]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        var resolution = response.Value.ShouldHaveSingleItem();
        resolution.CanonicalKey.ShouldBe(string.Empty);
        resolution.Confidence.ShouldBe(0);
        resolution.OptionValue.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_fields_populates_option_value_for_a_select_field_from_the_profile()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(EmptySnapshot(new ResumeBasics { FullName = "Jordan Rivera", Headline = "Senior Backend Engineer" }));

        var brief = FieldResolutionBrief(
            [("el-1", "select", "Current Job Title", null, null, null)],
            options: ["Senior Backend Engineer", "Product Manager"]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        var resolution = response.Value.ShouldHaveSingleItem();
        resolution.CanonicalKey.ShouldBe("currentTitle");
        resolution.OptionValue.ShouldBe("Senior Backend Engineer");
    }

    [Fact]
    public async Task Resolve_fields_populates_option_value_for_a_radio_field_from_the_profile()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(EmptySnapshot(new ResumeBasics { FullName = "Jordan Rivera", Headline = "Senior Backend Engineer" }));

        var brief = FieldResolutionBrief(
            [("el-1", "radio", "Current Job Title", null, null, null)],
            options: ["Senior Backend Engineer", "Product Manager"]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        response.Value.ShouldHaveSingleItem().OptionValue.ShouldBe("Senior Backend Engineer");
    }

    [Fact]
    public async Task Resolve_fields_leaves_option_value_null_when_the_profile_has_no_value_for_the_resolved_key()
    {
        var brief = FieldResolutionBrief(
            [("el-1", "select", "Work Authorization", null, null, null)],
            options: ["Yes, authorized", "No, I require sponsorship"]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        var resolution = response.Value.ShouldHaveSingleItem();
        resolution.CanonicalKey.ShouldBe("workAuthorization");
        resolution.OptionValue.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_fields_leaves_option_value_null_for_a_plain_text_field_even_with_a_resolvable_profile_value()
    {
        _knowledgeBaseReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(EmptySnapshot(new ResumeBasics { FullName = "Jordan Rivera", Email = "jordan@example.com" }));

        var brief = FieldResolutionBrief([("el-1", "email", "Email Address", null, null, null)]);

        var response = await _model.CompleteAsync<IReadOnlyList<FieldResolutionDto>>(NewFieldResolutionRequest(brief), CancellationToken.None);

        response.Value.ShouldHaveSingleItem().OptionValue.ShouldBeNull();
    }

    private static readonly IReadOnlyList<string> CanonicalKeys =
    [
        "firstName", "lastName", "fullName", "preferredName", "email", "phone",
        "addressLine1", "addressLine2", "city", "state", "postalCode", "country",
        "linkedin", "github", "portfolio", "website",
        "currentCompany", "currentTitle", "yearsExperience",
        "workAuthorization", "requiresSponsorship", "willingToRelocate",
        "noticePeriod", "desiredSalary", "availableStartDate",
        "gender", "ethnicity", "veteranStatus", "disabilityStatus",
        "howDidYouHear", "referredBy",
    ];

    /// <summary>Builds a brief in exactly the shape <c>AutofillEndpoints.BuildBrief</c> emits.</summary>
    private static string FieldResolutionBrief(
        IReadOnlyList<(string ElementId, string InputType, string? Label, string? Name, string? Placeholder, string? AutoComplete)> fields,
        IReadOnlyList<string>? options = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("CANONICAL-KEYS: ").Append(string.Join(',', CanonicalKeys)).Append('\n');
        sb.Append("HOST: boards.greenhouse.io\n");
        sb.Append("FIELDS\n");

        foreach (var (elementId, inputType, label, name, placeholder, autoComplete) in fields)
        {
            sb.Append(elementId).Append('|')
                .Append(inputType).Append('|')
                .Append(label).Append('|')
                .Append(name).Append('|')
                .Append(placeholder).Append('|')
                .Append(autoComplete).Append('|')
                .Append(string.Join(';', options ?? []))
                .Append('\n');
        }

        return sb.ToString();
    }

    private static ModelRequest NewFieldResolutionRequest(string brief) => new()
    {
        System = "You resolve web form fields to canonical autofill keys.",
        User = brief,
        SchemaName = JsonSchemaRegistry.FieldResolutionsSchemaName,
    };

    private sealed record FieldResolutionDto
    {
        public required string ElementId { get; init; }

        public required string CanonicalKey { get; init; }

        public required double Confidence { get; init; }

        public string? OptionValue { get; init; }
    }

    private static ModelRequest NewRequest(string brief) => new()
    {
        System = "You propose tailoring commands.",
        User = brief,
        SchemaName = JsonSchemaRegistry.TailorCommandsSchemaName,
    };
}
