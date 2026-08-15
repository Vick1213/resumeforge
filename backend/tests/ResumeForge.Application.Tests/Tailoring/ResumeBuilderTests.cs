using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="ResumeBuilder"/>.</summary>
public sealed class ResumeBuilderTests
{
    private static ResumeBuilder NewBuilder() =>
        new(FakeSkillTaxonomy.CreateDefault(), new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    private static KnowledgeBaseSnapshot Snapshot(IReadOnlyList<KnowledgeItem> items) => new()
    {
        Items = items,
        Basics = TestData.Basics("Jane Doe"),
        DefaultSummary = "Experienced engineer.",
        Diagnostics = [],
    };

    [Fact]
    public void Bullet_ids_round_trip_through_EntityId_Parse()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme-corp", "Engineer", "Acme",
            new DateOnly(2022, 1, 1), null, isCurrent: true,
            bullets:
            [
                TestData.KnowledgeBullet("First bullet."),
                TestData.KnowledgeBullet("Second bullet."),
                TestData.KnowledgeBullet("Third bullet."),
            ]);

        var doc = NewBuilder().Build(Snapshot([item]));

        var bullets = doc.Experience.Single().Bullets;
        bullets.Select(b => b.Id).ShouldBe(["exp:acme-corp#0", "exp:acme-corp#1", "exp:acme-corp#2"]);
        foreach (var bullet in bullets)
        {
            EntityId.Parse(bullet.Id).ToString().ShouldBe(bullet.Id);
        }
    }

    [Fact]
    public void Entry_id_round_trips()
    {
        var item = TestData.KnowledgeItem(KnowledgeItemType.Experience, "acme-corp", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true);
        var doc = NewBuilder().Build(Snapshot([item]));

        var entry = doc.Experience.Single();
        EntityId.Parse(entry.Id).ToString().ShouldBe(entry.Id);
        entry.Id.ShouldBe("exp:acme-corp");
    }

    [Fact]
    public void Current_roles_sort_first_regardless_of_input_order()
    {
        var old = TestData.KnowledgeItem(KnowledgeItemType.Experience, "old-co", "Engineer", "Old Co", new DateOnly(2018, 1, 1), new DateOnly(2020, 1, 1));
        var current = TestData.KnowledgeItem(KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true);
        var mid = TestData.KnowledgeItem(KnowledgeItemType.Experience, "mid-co", "Engineer", "Mid Co", new DateOnly(2020, 2, 1), new DateOnly(2022, 1, 1));

        var doc = NewBuilder().Build(Snapshot([old, mid, current]));

        doc.Experience.Select(e => e.Id).ShouldBe(["exp:acme", "exp:mid-co", "exp:old-co"]);
    }

    [Fact]
    public void Ordering_is_deterministic_regardless_of_input_list_order()
    {
        var a = TestData.KnowledgeItem(KnowledgeItemType.Experience, "a-co", "Engineer", "A Co", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1));
        var b = TestData.KnowledgeItem(KnowledgeItemType.Experience, "b-co", "Engineer", "B Co", new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1));

        var doc1 = NewBuilder().Build(Snapshot([a, b]));
        var doc2 = NewBuilder().Build(Snapshot([b, a]));

        doc1.Experience.Select(e => e.Id).ShouldBe(doc2.Experience.Select(e => e.Id));
    }

    [Fact]
    public void Skills_are_synthesized_from_tech_frontmatter_not_from_a_dedicated_source()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true,
            tech: ["C#", "Kubernetes"]);

        var doc = NewBuilder().Build(Snapshot([item]));

        doc.Skills.ShouldNotBeEmpty();
        doc.Skills.SelectMany(g => g.Items).Select(s => s.Normalized).ShouldBe(["csharp", "kubernetes"], ignoreOrder: true);
    }

    [Fact]
    public void Skill_groups_are_emitted_in_the_fixed_category_order()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true,
            tech: ["Docker", "Kubernetes", "C#", "PostgreSQL", "Agile"]);

        var doc = NewBuilder().Build(Snapshot([item]));

        // languages, frameworks, datastores, cloud, practices, tools, soft — omitting empties.
        doc.Skills.Select(g => g.Id).ShouldBe(["skl:languages", "skl:datastores", "skl:cloud", "skl:practices", "skl:tools"]);
    }

    [Fact]
    public void Skills_within_a_group_are_sorted_alphabetically_by_display_name()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true,
            tech: ["Python", "C#", "TypeScript"]);

        var doc = NewBuilder().Build(Snapshot([item]));

        var languages = doc.Skills.Single(g => g.Id == "skl:languages");
        languages.Items.Select(s => s.Name).ShouldBe(["C#", "Python", "TypeScript"]);
    }

    [Fact]
    public void Unrecognized_tech_goes_into_a_trailing_other_group()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true,
            tech: ["C#", "SomeUnknownFramework"]);

        var doc = NewBuilder().Build(Snapshot([item]));

        doc.Skills.Last().Id.ShouldBe("skl:other");
        doc.Skills.Last().Label.ShouldBe("Other");
        doc.Skills.Last().Items.Single().Name.ShouldBe("SomeUnknownFramework");
    }

    [Fact]
    public void Skill_ids_follow_category_and_canonical_name()
    {
        var item = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true,
            tech: ["C#"]);

        var doc = NewBuilder().Build(Snapshot([item]));

        doc.Skills.Single().Items.Single().Id.ShouldBe("skl:languages#csharp");
    }

    [Fact]
    public void Same_skill_from_experience_and_project_is_deduplicated()
    {
        var exp = TestData.KnowledgeItem(
            KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true, tech: ["C#"]);
        var prj = TestData.KnowledgeItem(KnowledgeItemType.Project, "side-project", "Side Project", tech: ["C#"]);

        var doc = NewBuilder().Build(Snapshot([exp, prj]));

        doc.Skills.Single().Items.Count.ShouldBe(1);
    }

    [Fact]
    public void No_tech_anywhere_produces_no_skill_groups()
    {
        var item = TestData.KnowledgeItem(KnowledgeItemType.Experience, "acme", "Engineer", "Acme", new DateOnly(2022, 1, 1), null, isCurrent: true);
        var doc = NewBuilder().Build(Snapshot([item]));

        doc.Skills.ShouldBeEmpty();
    }

    [Fact]
    public void Basics_and_default_summary_are_carried_through()
    {
        var doc = NewBuilder().Build(Snapshot([]));

        doc.Basics.FullName.ShouldBe("Jane Doe");
        doc.Summary.ShouldBe("Experienced engineer.");
    }

    [Fact]
    public void Section_order_has_the_documented_default()
    {
        var doc = NewBuilder().Build(Snapshot([]));

        doc.SectionOrder.ShouldBe(
        [
            SectionKind.Summary, SectionKind.Skills, SectionKind.Experience,
            SectionKind.Projects, SectionKind.Education, SectionKind.Certifications,
        ]);
    }

    [Fact]
    public void Education_and_certification_entries_are_built()
    {
        var edu = TestData.KnowledgeItem(KnowledgeItemType.Education, "uw-madison", "B.S. Computer Science", "UW-Madison");
        var cert = TestData.KnowledgeItem(KnowledgeItemType.Certification, "az-204", "AZ-204", "Microsoft");

        var doc = NewBuilder().Build(Snapshot([edu, cert]));

        doc.Education.Single().Id.ShouldBe("edu:uw-madison");
        doc.Certifications.Single().Id.ShouldBe("cert:az-204");
    }

    [Fact]
    public void Explicit_id_and_name_are_used_when_provided()
    {
        var doc = NewBuilder().Build(Snapshot([]), id: "resume-42", name: "Custom Resume");

        doc.Id.ShouldBe("resume-42");
        doc.Name.ShouldBe("Custom Resume");
    }
}
