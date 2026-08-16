using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="CommandValidator"/>, covering the five validation rules from CONTRACTS.md §6.</summary>
public sealed class CommandValidatorTests
{
    private readonly CommandValidator _validator = new(new FabricationGuard());

    private static ResumeDocument NewDocument()
    {
        var bullet0 = TestData.Bullet("exp:acme#0", "Cut p99 latency from 840ms to 120ms.", variants: ["Rebuilt the fan-out, cutting p99 to 120ms.", "Improved checkout latency 7x."]);
        var bullet1 = TestData.Bullet("exp:acme#1", "Led migration of 40 services to .NET 8.");
        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: [bullet0, bullet1]);

        var skill = TestData.Skill("skl:languages#csharp", "C#", "csharp");
        var group = TestData.SkillGroup("skl:languages", "Languages", [skill]);

        return TestData.Document(experience: [entry], skills: [group]);
    }

    private static TailorOptions Options(int maxRewrites = 6, ModelEffort effort = ModelEffort.Standard) =>
        new() { MaxRewrites = maxRewrites, Effort = effort };

    [Fact]
    public void Valid_include_command_is_accepted()
    {
        var doc = NewDocument();
        var command = new IncludeCommand { Targets = ["exp:acme"] };

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBe([command]);
        result.Rejected.ShouldBeEmpty();
    }

    [Fact]
    public void Include_command_with_unknown_target_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new IncludeCommand { Targets = ["exp:ghost"] };

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBeEmpty();
        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Fact]
    public void Exclude_command_with_malformed_id_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new ExcludeCommand { Targets = ["not-a-valid-id"] };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Fact]
    public void Select_variant_within_range_is_accepted()
    {
        var doc = NewDocument();
        var command = new SelectVariantCommand { Target = "exp:acme#0", VariantIndex = 1 };

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBe([command]);
    }

    [Fact]
    public void Select_variant_out_of_range_is_rejected_with_variant_index_out_of_range_code()
    {
        var doc = NewDocument();
        var command = new SelectVariantCommand { Target = "exp:acme#0", VariantIndex = 5 };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("variant-index-out-of-range");
    }

    [Fact]
    public void Select_variant_on_unknown_bullet_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new SelectVariantCommand { Target = "exp:acme#9", VariantIndex = 0 };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Fact]
    public void Rewrite_within_limits_and_passing_fabrication_check_is_accepted()
    {
        var doc = NewDocument();
        var command = new RewriteCommand { Target = "exp:acme#0", Text = "Rebuilt the fan-out, cutting p99 latency to 120ms." };

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBe([command]);
    }

    [Fact]
    public void Rewrite_exceeding_300_characters_is_rejected_with_rewrite_too_long_code()
    {
        var doc = NewDocument();
        var command = new RewriteCommand { Target = "exp:acme#0", Text = new string('a', 301) };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("rewrite-too-long");
    }

    [Fact]
    public void Rewrite_with_newline_is_rejected_with_rewrite_multiline_code()
    {
        var doc = NewDocument();
        var command = new RewriteCommand { Target = "exp:acme#0", Text = "Line one\nLine two" };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("rewrite-multiline");
    }

    [Fact]
    public void Rewrite_that_fabricates_a_metric_is_rejected_with_fabricated_metric_code()
    {
        var doc = NewDocument();
        var command = new RewriteCommand { Target = "exp:acme#1", Text = "Reduced infrastructure costs by 60%." };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("fabricated-metric");
    }

    [Fact]
    public void Rewrite_count_beyond_max_rewrites_rejects_the_excess_deterministically()
    {
        var doc = NewDocument();
        var commands = new TailorCommand[]
        {
            new RewriteCommand { Target = "exp:acme#0", Text = "Cut p99 latency to 120ms through a fan-out rebuild." },
            new RewriteCommand { Target = "exp:acme#1", Text = "Migrated 40 services onto .NET 8." },
            new IncludeCommand { Targets = ["exp:acme"] },
        };

        var result = _validator.Validate(commands, doc, Options(maxRewrites: 1));

        result.Accepted.OfType<RewriteCommand>().Count().ShouldBe(1);
        result.Accepted.OfType<RewriteCommand>().Single().ShouldBe(commands[0]);
        result.Accepted.OfType<IncludeCommand>().ShouldNotBeEmpty();
        result.Rejected.Single().Code.ShouldBe("rewrite-limit-exceeded");
    }

    [Fact]
    public void Order_command_with_duplicate_children_is_rejected_with_duplicate_order_entry_code()
    {
        var doc = NewDocument();
        var command = new OrderCommand { Parent = "exp:acme", Order = ["exp:acme#0", "exp:acme#0"] };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("duplicate-order-entry");
    }

    [Fact]
    public void Order_command_with_root_parent_bypasses_entity_id_resolution_for_the_parent()
    {
        var doc = NewDocument();
        var command = new OrderCommand { Parent = "root", Order = ["exp:acme"] };

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBe([command]);
    }

    [Fact]
    public void Order_command_with_unknown_child_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new OrderCommand { Parent = "exp:acme", Order = ["exp:acme#0", "exp:acme#99"] };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Fact]
    public void Order_command_with_unknown_non_root_parent_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new OrderCommand { Parent = "exp:ghost", Order = [] };

        var result = _validator.Validate([command], doc, Options());

        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Theory]
    [MemberData(nameof(UnconditionallyAcceptedCommands))]
    public void Commands_with_no_target_resolution_rule_are_always_accepted(TailorCommand command)
    {
        var doc = NewDocument();

        var result = _validator.Validate([command], doc, Options());

        result.Accepted.ShouldBe([command]);
        result.Rejected.ShouldBeEmpty();
    }

    public static TheoryData<TailorCommand> UnconditionallyAcceptedCommands() => new()
    {
        new SetSummaryCommand { Text = "A concise professional summary." },
        new EmphasizeSkillsCommand { Skills = ["csharp"] },
        new SetSectionOrderCommand { Order = [SectionKind.Summary, SectionKind.Experience] },
    };

    [Fact]
    public void InjectKeywords_with_an_evidenced_keyword_at_thorough_effort_is_accepted()
    {
        var doc = NewDocument();
        // "csharp" is evidenced by the skl:languages#csharp skill in the fixture document.
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["csharp"],
            Text = "Led migration of 40 services to .NET 8, written in C#.",
        };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Thorough));

        result.Accepted.ShouldBe([command]);
        result.Rejected.ShouldBeEmpty();
    }

    [Fact]
    public void InjectKeywords_evidenced_only_by_bullet_text_at_thorough_effort_is_accepted()
    {
        var doc = NewDocument();
        // "latency" appears nowhere as a skill, only inside exp:acme#0's own bullet text —
        // still evidence per rule 6 ("...or in the text of some entry or bullet"). The
        // injection targets exp:acme#1 (a different bullet) and introduces no digit-bearing
        // token beyond what exp:acme#1 already had, so it also clears rule 3's guard.
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["latency"],
            Text = "Led migration of 40 services to .NET 8, tracking latency along the way.",
        };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Thorough));

        result.Accepted.ShouldBe([command]);
    }

    [Fact]
    public void InjectKeywords_below_thorough_effort_is_rejected_with_op_unavailable_at_effort_code()
    {
        var doc = NewDocument();
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["csharp"],
            Text = "Led migration of 40 services to .NET 8, written in C#.",
        };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Standard));

        result.Rejected.Single().Code.ShouldBe("op-unavailable-at-effort");
    }

    [Fact]
    public void InjectKeywords_naming_a_keyword_absent_from_the_kb_is_rejected_even_at_maximum_effort()
    {
        // The line this rule exists to hold: keyword optimization must never become
        // fabrication, and that guarantee does not relax at the highest effort level.
        var doc = NewDocument();
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["kubernetes"],
            Text = "Led migration of 40 services to .NET 8, orchestrated with Kubernetes.",
        };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Maximum));

        result.Accepted.ShouldBeEmpty();
        result.Rejected.Single().Code.ShouldBe("unsupported-keyword");
    }

    [Fact]
    public void InjectKeywords_with_an_unresolvable_target_is_rejected_with_unknown_target_code()
    {
        var doc = NewDocument();
        var command = new InjectKeywordsCommand { Target = "exp:acme#99", Keywords = ["csharp"], Text = "Whatever." };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Thorough));

        result.Rejected.Single().Code.ShouldBe("unknown-target");
    }

    [Fact]
    public void InjectKeywords_that_fabricates_a_metric_is_rejected_with_fabricated_metric_code()
    {
        var doc = NewDocument();
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["csharp"],
            Text = "Reduced infrastructure costs by 60% using C#.",
        };

        var result = _validator.Validate([command], doc, Options(effort: ModelEffort.Thorough));

        result.Rejected.Single().Code.ShouldBe("fabricated-metric");
    }

    [Fact]
    public void A_batch_of_entirely_valid_commands_is_fully_accepted()
    {
        var doc = NewDocument();
        var commands = new TailorCommand[]
        {
            new IncludeCommand { Targets = ["exp:acme"] },
            new SelectVariantCommand { Target = "exp:acme#0", VariantIndex = 0 },
            new SetSummaryCommand { Text = "Backend engineer focused on distributed systems." },
        };

        var result = _validator.Validate(commands, doc, Options());

        result.Accepted.Count.ShouldBe(3);
        result.Rejected.ShouldBeEmpty();
    }
}
