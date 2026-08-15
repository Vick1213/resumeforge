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

    private static TailorOptions Options(int maxRewrites = 6) => new() { MaxRewrites = maxRewrites };

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
