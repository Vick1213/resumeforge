using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="CommandExecutor"/>.</summary>
public sealed class CommandExecutorTests
{
    private readonly CommandExecutor _executor = new(new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    private static ResumeDocument NewDocument()
    {
        var bullets = new[]
        {
            TestData.Bullet("exp:acme#0", "Bullet zero.", variants: ["Bullet zero variant."]),
            TestData.Bullet("exp:acme#1", "Bullet one."),
            TestData.Bullet("exp:acme#2", "Bullet two."),
            TestData.Bullet("exp:acme#3", "Bullet three."),
        };

        var entry = TestData.Experience("exp:acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, bullets: bullets, included: true);
        var otherEntry = TestData.Experience("exp:other", "Engineer", "Other Co", new DateOnly(2019, 1, 1), new DateOnly(2021, 1, 1));

        var skill1 = TestData.Skill("skl:languages#csharp", "C#", "csharp");
        var skill2 = TestData.Skill("skl:languages#python", "Python", "python");
        var group = TestData.SkillGroup("skl:languages", "Languages", [skill1, skill2]);

        return TestData.Document(experience: [entry, otherEntry], skills: [group], summary: "Old summary.");
    }

    [Fact]
    public void Execute_never_mutates_the_input_document()
    {
        var doc = NewDocument();
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(doc);

        _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme"] }, new ExcludeCommand { Targets = ["exp:acme#1"] }]);

        System.Text.Json.JsonSerializer.Serialize(doc).ShouldBe(snapshotJson);
    }

    [Fact]
    public void Exclude_on_an_entry_flips_included_but_keeps_it_in_the_document()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme"] }]);

        var entry = result.Document.Experience.Single(e => e.Id == "exp:acme");
        entry.Included.ShouldBeFalse();

        var diff = result.Diff.Single();
        diff.Kind.ShouldBe(DiffKind.Excluded);
        diff.EntityId.ShouldBe("exp:acme");
    }

    [Fact]
    public void Include_on_a_previously_excluded_entry_flips_it_back()
    {
        var doc = NewDocument();
        doc = doc with { Experience = [.. doc.Experience.Select(e => e.Id == "exp:acme" ? e with { Included = false } : e)] };

        var result = _executor.Execute(doc, [new IncludeCommand { Targets = ["exp:acme"] }]);

        result.Document.Experience.Single(e => e.Id == "exp:acme").Included.ShouldBeTrue();
        result.Diff.Single().Kind.ShouldBe(DiffKind.Included);
    }

    [Fact]
    public void Exclude_on_a_bullet_removes_it_from_the_list_without_renumbering_siblings()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme#1"] }]);

        var entry = result.Document.Experience.Single(e => e.Id == "exp:acme");
        entry.Bullets.Select(b => b.Id).ShouldBe(["exp:acme#0", "exp:acme#2", "exp:acme#3"]);

        var diff = result.Diff.Single();
        diff.Kind.ShouldBe(DiffKind.Excluded);
        diff.EntityId.ShouldBe("exp:acme#1");
        diff.Before.ShouldBe("Bullet one.");
    }

    [Fact]
    public void Excluding_the_middle_bullet_of_three_never_renumbers_the_others()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme#1"] }, new ExcludeCommand { Targets = ["exp:acme#2"] }]);

        var entry = result.Document.Experience.Single(e => e.Id == "exp:acme");
        entry.Bullets.Select(b => b.Id).ShouldBe(["exp:acme#0", "exp:acme#3"]);
    }

    [Fact]
    public void Exclude_then_include_of_the_same_bullet_restores_its_original_text_and_position()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme#1"] }, new IncludeCommand { Targets = ["exp:acme#1"] }]);

        var entry = result.Document.Experience.Single(e => e.Id == "exp:acme");
        entry.Bullets.Select(b => b.Id).ShouldBe(["exp:acme#0", "exp:acme#1", "exp:acme#2", "exp:acme#3"]);
        entry.Bullets.Single(b => b.Id == "exp:acme#1").Text.ShouldBe("Bullet one.");
        result.Diff.Count.ShouldBe(2);
        result.Diff[0].Kind.ShouldBe(DiffKind.Excluded);
        result.Diff[1].Kind.ShouldBe(DiffKind.Included);
    }

    [Fact]
    public void Exclude_on_an_individual_skill_removes_it_from_its_group()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["skl:languages#python"] }]);

        var group = result.Document.Skills.Single(g => g.Id == "skl:languages");
        group.Items.Select(s => s.Id).ShouldBe(["skl:languages#csharp"]);
        group.Included.ShouldBeTrue();
    }

    [Fact]
    public void Exclude_on_a_skill_group_keeps_the_group_and_its_skills_but_flips_included()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["skl:languages"] }]);

        var group = result.Document.Skills.Single(g => g.Id == "skl:languages");
        group.Included.ShouldBeFalse();
        group.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void Select_variant_replaces_bullet_text_and_records_variant_selected_diff()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new SelectVariantCommand { Target = "exp:acme#0", VariantIndex = 0 }]);

        var bullet = result.Document.Experience.Single(e => e.Id == "exp:acme").Bullets.Single(b => b.Id == "exp:acme#0");
        bullet.Text.ShouldBe("Bullet zero variant.");

        var diff = result.Diff.Single();
        diff.Kind.ShouldBe(DiffKind.VariantSelected);
        diff.Before.ShouldBe("Bullet zero.");
        diff.After.ShouldBe("Bullet zero variant.");
    }

    [Fact]
    public void Rewrite_replaces_bullet_text_and_records_rewritten_diff()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new RewriteCommand { Target = "exp:acme#1", Text = "A rewritten bullet." }]);

        var bullet = result.Document.Experience.Single(e => e.Id == "exp:acme").Bullets.Single(b => b.Id == "exp:acme#1");
        bullet.Text.ShouldBe("A rewritten bullet.");
        result.Diff.Single().Kind.ShouldBe(DiffKind.Rewritten);
    }

    [Fact]
    public void Inject_keywords_replaces_bullet_text_and_records_keywords_injected_diff_with_keywords_populated()
    {
        var doc = NewDocument();
        var command = new InjectKeywordsCommand
        {
            Target = "exp:acme#1",
            Keywords = ["kubernetes", "postgresql"],
            Text = "Bullet one, now mentioning Kubernetes and PostgreSQL.",
        };

        var result = _executor.Execute(doc, [command]);

        var bullet = result.Document.Experience.Single(e => e.Id == "exp:acme").Bullets.Single(b => b.Id == "exp:acme#1");
        bullet.Text.ShouldBe("Bullet one, now mentioning Kubernetes and PostgreSQL.");

        var diff = result.Diff.Single();
        diff.Kind.ShouldBe(DiffKind.KeywordsInjected);
        diff.EntityId.ShouldBe("exp:acme#1");
        diff.Before.ShouldBe("Bullet one.");
        diff.After.ShouldBe("Bullet one, now mentioning Kubernetes and PostgreSQL.");
        diff.Keywords.ShouldBe(["kubernetes", "postgresql"]);
    }

    [Fact]
    public void Rewritten_and_variant_selected_diffs_carry_no_keywords()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new RewriteCommand { Target = "exp:acme#1", Text = "A rewritten bullet." }]);

        result.Diff.Single().Keywords.ShouldBeEmpty();
    }

    [Fact]
    public void Set_summary_updates_summary_and_records_diff()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new SetSummaryCommand { Text = "New summary." }]);

        result.Document.Summary.ShouldBe("New summary.");
        var diff = result.Diff.Single();
        diff.Kind.ShouldBe(DiffKind.SummarySet);
        diff.Before.ShouldBe("Old summary.");
        diff.After.ShouldBe("New summary.");
    }

    [Fact]
    public void Set_tagline_updates_the_project_description_and_records_diff()
    {
        var project = TestData.Project("prj:tinyorm", "TinyORM", tagline: "A minimal ORM.");
        var doc = NewDocument() with { Projects = [project] };

        var result = _executor.Execute(doc, [new SetTaglineCommand { Target = "prj:tinyorm", Text = "A minimal ORM for small services." }]);

        result.Document.Projects.Single().Tagline.ShouldBe("A minimal ORM for small services.");
        var diff = result.Diff.Single(d => d.Kind == DiffKind.TaglineSet);
        diff.Before.ShouldBe("A minimal ORM.");
        diff.After.ShouldBe("A minimal ORM for small services.");
    }

    [Fact]
    public void Set_tagline_naming_an_unknown_project_changes_nothing()
    {
        var doc = NewDocument() with { Projects = [TestData.Project("prj:tinyorm", "TinyORM", tagline: "A minimal ORM.")] };

        var result = _executor.Execute(doc, [new SetTaglineCommand { Target = "prj:nope", Text = "Something else." }]);

        result.Document.Projects.Single().Tagline.ShouldBe("A minimal ORM.");
        result.Diff.ShouldNotContain(d => d.Kind == DiffKind.TaglineSet);
    }

    [Fact]
    public void Multiple_set_summary_commands_the_last_one_wins()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc,
        [
            new SetSummaryCommand { Text = "First." },
            new SetSummaryCommand { Text = "Second." },
        ]);

        result.Document.Summary.ShouldBe("Second.");
    }

    [Fact]
    public void Emphasize_skills_sets_emphasized_and_records_diff()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new EmphasizeSkillsCommand { Skills = ["python"] }]);

        var skill = result.Document.Skills.Single(g => g.Id == "skl:languages").Items.Single(s => s.Id == "skl:languages#python");
        skill.Emphasized.ShouldBeTrue();
        result.Diff.Single().Kind.ShouldBe(DiffKind.SkillEmphasized);
    }

    [Fact]
    public void Set_section_order_updates_order_and_records_reordered_diff()
    {
        var doc = NewDocument();
        var newOrder = new[] { SectionKind.Experience, SectionKind.Summary };
        var result = _executor.Execute(doc, [new SetSectionOrderCommand { Order = newOrder }]);

        result.Document.SectionOrder.ShouldBe(newOrder);
        result.Diff.Single().Kind.ShouldBe(DiffKind.Reordered);
    }

    [Fact]
    public void Order_command_reorders_bullets_within_an_entry()
    {
        var doc = NewDocument();
        var order = new OrderCommand { Parent = "exp:acme", Order = ["exp:acme#2", "exp:acme#0"] };

        var result = _executor.Execute(doc, [order]);

        var entry = result.Document.Experience.Single(e => e.Id == "exp:acme");
        entry.Bullets.Select(b => b.Id).ShouldBe(["exp:acme#2", "exp:acme#0", "exp:acme#1", "exp:acme#3"]);
    }

    [Fact]
    public void Order_command_with_omitted_children_keeps_them_after_the_listed_ones_in_original_relative_order()
    {
        var doc = NewDocument();
        // Only #2 and #0 are listed; #1 and #3 are omitted and must follow, in their
        // original relative order (#1 before #3).
        var order = new OrderCommand { Parent = "exp:acme", Order = ["exp:acme#2", "exp:acme#0"] };

        var result = _executor.Execute(doc, [order]);

        var ids = result.Document.Experience.Single(e => e.Id == "exp:acme").Bullets.Select(b => b.Id).ToList();
        ids.ShouldBe(["exp:acme#2", "exp:acme#0", "exp:acme#1", "exp:acme#3"]);
    }

    [Fact]
    public void Order_command_with_root_parent_reorders_experience_entries_by_kind_of_listed_ids()
    {
        var doc = NewDocument();
        var order = new OrderCommand { Parent = "root", Order = ["exp:other", "exp:acme"] };

        var result = _executor.Execute(doc, [order]);

        result.Document.Experience.Select(e => e.Id).ShouldBe(["exp:other", "exp:acme"]);
    }

    [Fact]
    public void Includes_and_excludes_always_apply_before_ordering_regardless_of_input_order()
    {
        var doc = NewDocument();
        // The order command lists a bullet the exclude command removes; because
        // includes/excludes apply first regardless of position in the input list, the
        // excluded id is simply absent from the final order.
        var commands = new TailorCommand[]
        {
            new OrderCommand { Parent = "exp:acme", Order = ["exp:acme#2", "exp:acme#0", "exp:acme#1"] },
            new ExcludeCommand { Targets = ["exp:acme#1"] },
        };

        var result = _executor.Execute(doc, commands);

        var ids = result.Document.Experience.Single(e => e.Id == "exp:acme").Bullets.Select(b => b.Id).ToList();
        ids.ShouldBe(["exp:acme#2", "exp:acme#0", "exp:acme#3"]);
    }

    [Fact]
    public void Rationale_is_carried_through_to_the_diff_entry()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme"], Rationale = "Not relevant to this role." }]);

        result.Diff.Single().Rationale.ShouldBe("Not relevant to this role.");
    }

    [Fact]
    public void Unrecognized_or_stale_target_is_silently_ignored_rather_than_throwing()
    {
        var doc = NewDocument();

        Should.NotThrow(() => _executor.Execute(doc, [new ExcludeCommand { Targets = ["exp:acme#99"] }]));
    }

    [Fact]
    public void Document_id_and_name_are_preserved_across_execution()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, [new SetSummaryCommand { Text = "Updated." }]);

        result.Document.Id.ShouldBe(doc.Id);
        result.Document.Name.ShouldBe(doc.Name);
        result.Document.CreatedAt.ShouldBe(doc.CreatedAt);
    }

    [Fact]
    public void No_commands_produces_no_diff_and_an_equivalent_document()
    {
        var doc = NewDocument();
        var result = _executor.Execute(doc, []);

        result.Diff.ShouldBeEmpty();
        System.Text.Json.JsonSerializer.Serialize(result.Document with { UpdatedAt = doc.UpdatedAt })
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(doc));
    }
}
