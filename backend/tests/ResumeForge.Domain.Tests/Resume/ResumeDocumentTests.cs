using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Resume;

/// <summary>
/// Tests for the query helpers on <see cref="ResumeDocument"/>.
/// </summary>
public sealed class ResumeDocumentTests
{
    private static ResumeDocument BuildDocument() => new()
    {
        Id = "11111111-1111-1111-1111-111111111111",
        Name = "Base resume",
        Basics = new ResumeBasics { FullName = "Jamie Rivera" },
        Summary = "Backend engineer focused on distributed systems.",
        Skills =
        [
            new SkillGroup
            {
                Id = "skl:languages",
                Label = "Languages",
                Items =
                [
                    new Skill { Id = "skl:languages#csharp", Name = "C#", Normalized = "csharp" },
                    new Skill { Id = "skl:languages#python", Name = "Python", Normalized = "python" },
                ],
            },
        ],
        Experience =
        [
            new ExperienceEntry
            {
                Id = "exp:acme-corp",
                Role = "Senior Software Engineer",
                Organization = "Acme Corp",
                StartDate = new DateOnly(2022, 3, 1),
                Bullets =
                [
                    new Bullet { Id = "exp:acme-corp#0", Text = "Cut p99 checkout latency by 85%." },
                    new Bullet { Id = "exp:acme-corp#1", Text = "Led migration of 40+ services to .NET 8." },
                ],
            },
        ],
        Projects =
        [
            new ProjectEntry
            {
                Id = "prj:graph-runner",
                Name = "Graph Runner",
                Bullets =
                [
                    new Bullet { Id = "prj:graph-runner#0", Text = "Built a barrier-free DAG scheduler." },
                ],
            },
        ],
        Education =
        [
            new EducationEntry
            {
                Id = "edu:uw-madison",
                Institution = "University of Wisconsin–Madison",
                Credential = "B.S. Computer Science",
            },
        ],
        Certifications =
        [
            new CertificationEntry { Id = "cert:az-204", Name = "Azure Developer Associate" },
        ],
        SectionOrder =
        [
            SectionKind.Summary, SectionKind.Skills, SectionKind.Experience,
            SectionKind.Projects, SectionKind.Education, SectionKind.Certifications,
        ],
    };

    [Theory]
    [InlineData("sum")]
    [InlineData("exp:acme-corp")]
    [InlineData("exp:acme-corp#0")]
    [InlineData("exp:acme-corp#1")]
    [InlineData("prj:graph-runner")]
    [InlineData("prj:graph-runner#0")]
    [InlineData("edu:uw-madison")]
    [InlineData("cert:az-204")]
    [InlineData("skl:languages")]
    [InlineData("skl:languages#csharp")]
    [InlineData("skl:languages#python")]
    public void ContainsNode_returns_true_for_addressable_nodes(string idText)
    {
        var document = BuildDocument();
        document.ContainsNode(EntityId.Parse(idText)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("exp:unknown-corp")]
    [InlineData("exp:acme-corp#5")]
    [InlineData("prj:unknown-project")]
    [InlineData("edu:uw-madison#0")]
    [InlineData("cert:unknown-cert")]
    [InlineData("skl:unknown-group")]
    [InlineData("skl:languages#rust")]
    [InlineData("skl:unknown-group#csharp")]
    public void ContainsNode_returns_false_for_missing_nodes(string idText)
    {
        var document = BuildDocument();
        document.ContainsNode(EntityId.Parse(idText)).ShouldBeFalse();
    }

    [Fact]
    public void TryFindBullet_locates_experience_bullet()
    {
        var document = BuildDocument();

        document.TryFindBullet(EntityId.Parse("exp:acme-corp#1"), out var bullet).ShouldBeTrue();
        bullet.Text.ShouldBe("Led migration of 40+ services to .NET 8.");
    }

    [Fact]
    public void TryFindBullet_locates_project_bullet()
    {
        var document = BuildDocument();

        document.TryFindBullet(EntityId.Parse("prj:graph-runner#0"), out var bullet).ShouldBeTrue();
        bullet.Text.ShouldBe("Built a barrier-free DAG scheduler.");
    }

    [Fact]
    public void TryFindBullet_returns_false_for_missing_bullet()
    {
        var document = BuildDocument();

        document.TryFindBullet(EntityId.Parse("exp:acme-corp#9"), out _).ShouldBeFalse();
    }

    [Fact]
    public void EnumerateBullets_returns_every_bullet_in_the_document()
    {
        var document = BuildDocument();

        var bullets = document.EnumerateBullets().ToList();

        bullets.Count.ShouldBe(3);
        bullets.ShouldContain((EntityId.Parse("exp:acme-corp#0"), "Cut p99 checkout latency by 85%."));
        bullets.ShouldContain((EntityId.Parse("exp:acme-corp#1"), "Led migration of 40+ services to .NET 8."));
        bullets.ShouldContain((EntityId.Parse("prj:graph-runner#0"), "Built a barrier-free DAG scheduler."));
    }
}
