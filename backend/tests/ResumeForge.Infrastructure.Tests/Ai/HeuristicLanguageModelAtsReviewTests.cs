using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Skills;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="HeuristicLanguageModel"/>'s no-network <c>"ats-review"</c> path: the
/// offline stand-in for the screener pass, which can check term overlap and where a term
/// appears, and says plainly that it cannot do the part that needs judgement.
/// </summary>
public sealed class HeuristicLanguageModelAtsReviewTests
{
    private readonly HeuristicLanguageModel _model = new(
        new TailorOptions(),
        StubReader(),
        new SkillTaxonomy());

    private static IKnowledgeBaseReader StubReader()
    {
        var reader = Substitute.For<IKnowledgeBaseReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(new KnowledgeBaseSnapshot
        {
            Items = [],
            Basics = new ResumeBasics { FullName = "Jordan Rivera" },
            Diagnostics = [],
        });
        return reader;
    }

    private Task<ModelResponse<AtsReview>> ReviewAsync(string posting, string resume) =>
        _model.CompleteAsync<AtsReview>(
            new ModelRequest
            {
                System = "unused",
                User = $"JOB TITLE: Engineer\nCOMPANY: Acme\n\nJOB POSTING\n{posting}\n\nRESUME\n{resume}",
                SchemaName = "ats-review",
            },
            CancellationToken.None);

    [Fact]
    public async Task A_term_the_posting_wants_and_the_resume_never_mentions_is_a_critical_gap()
    {
        var review = await ReviewAsync(
            posting: "We need strong Kubernetes experience.",
            resume: "## Experience\n\n- Built a payments API in Python.\n");

        var gap = review.Value.Gaps.ShouldHaveSingleItem();
        gap.Keyword.ShouldContain("Kubernetes", Case.Insensitive);
        gap.Importance.ShouldBe(AtsGapImportance.Critical);
        gap.SkillsOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task A_term_listed_only_in_the_skills_section_is_still_a_gap_and_is_flagged_skills_only()
    {
        // The gap this whole pass exists to surface: the parser is satisfied and the human
        // has been told nothing about what the candidate actually did with the technology.
        var review = await ReviewAsync(
            posting: "We need strong Kubernetes experience.",
            resume: "## Skills\n\n- **Cloud:** Kubernetes\n\n## Experience\n\n- Built a payments API in Python.\n");

        var gap = review.Value.Gaps.ShouldHaveSingleItem();
        gap.Keyword.ShouldContain("Kubernetes", Case.Insensitive);
        gap.SkillsOnly.ShouldBeTrue();
        review.Value.RecruiterNotes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_term_evidenced_inside_a_bullet_is_not_a_gap()
    {
        var review = await ReviewAsync(
            posting: "We need strong Kubernetes experience.",
            resume: "## Experience\n\n- Cut deploy time 40% by moving 30 services onto Kubernetes.\n");

        review.Value.Gaps.ShouldBeEmpty();
        review.Value.ScoreBefore.ShouldBe(100);
    }

    [Fact]
    public async Task Closing_the_gaps_scores_higher_than_leaving_them_open()
    {
        var review = await ReviewAsync(
            posting: "We need Kubernetes, Terraform, and Go.",
            resume: "## Skills\n\n- **Cloud:** Kubernetes\n\n## Experience\n\n- Built a payments API in Python.\n");

        review.Value.ScoreAfter.ShouldBeGreaterThan(review.Value.ScoreBefore);
    }

    [Fact]
    public async Task An_offline_review_never_invents_an_angle_it_cannot_support()
    {
        // The angle is read by a pass with authority to rewrite bullets, so an offline
        // implementation that guessed at one would be manufacturing licence to fabricate.
        var review = await ReviewAsync(
            posting: "We need strong Kubernetes experience.",
            resume: "## Experience\n\n- Built a payments API in Python.\n");

        review.Value.Gaps.ShouldAllBe(g => g.Angle.Contains("Offline review", StringComparison.Ordinal)
            || g.Angle.Contains("offline review", StringComparison.Ordinal));
    }
}
