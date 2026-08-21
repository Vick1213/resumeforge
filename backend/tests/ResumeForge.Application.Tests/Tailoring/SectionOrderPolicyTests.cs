using ResumeForge.Application.Tailoring;
using ResumeForge.Application.Tests.TestSupport;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="SectionOrderPolicy"/>.</summary>
public sealed class SectionOrderPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

    private static readonly SectionKind[] ExperienceFirst =
    [
        SectionKind.Summary, SectionKind.Skills, SectionKind.Experience,
        SectionKind.Projects, SectionKind.Education, SectionKind.Certifications,
    ];

    private static EducationEntry Degree(DateOnly? end, bool included = true) =>
        TestData.Education("edu:asu", "Arizona State University", "B.S. Computer Science", new DateOnly(2022, 8, 1), end, included);

    [Fact]
    public void A_degree_still_in_progress_leads() =>
        SectionOrderPolicy.EducationLeads([Degree(end: null)], Now).ShouldBeTrue();

    [Fact]
    public void A_graduation_date_in_the_future_leads() =>
        SectionOrderPolicy.EducationLeads([Degree(new DateOnly(2027, 5, 1))], Now).ShouldBeTrue();

    [Fact]
    public void A_degree_inside_the_early_career_window_leads() =>
        SectionOrderPolicy.EducationLeads([Degree(new DateOnly(2026, 5, 1))], Now).ShouldBeTrue();

    [Fact]
    public void A_degree_past_the_early_career_window_does_not_lead() =>
        SectionOrderPolicy.EducationLeads([Degree(new DateOnly(2023, 5, 1))], Now).ShouldBeFalse();

    [Fact]
    public void An_excluded_degree_is_ignored() =>
        SectionOrderPolicy.EducationLeads([Degree(end: null, included: false)], Now).ShouldBeFalse();

    [Fact]
    public void A_document_with_no_education_never_leads_with_it() =>
        SectionOrderPolicy.EducationLeads([], Now).ShouldBeFalse();

    [Fact]
    public void Education_is_hoisted_to_just_after_the_summary() =>
        SectionOrderPolicy.Normalize(ExperienceFirst, educationLeads: true).ShouldBe(
        [
            SectionKind.Summary, SectionKind.Education, SectionKind.Skills,
            SectionKind.Experience, SectionKind.Projects, SectionKind.Certifications,
        ]);

    [Fact]
    public void Education_leads_outright_when_there_is_no_summary() =>
        SectionOrderPolicy.Normalize(
            [SectionKind.Skills, SectionKind.Experience, SectionKind.Education], educationLeads: true).ShouldBe(
            [SectionKind.Education, SectionKind.Skills, SectionKind.Experience]);

    [Fact]
    public void Every_other_section_keeps_its_relative_position() =>
        SectionOrderPolicy.Normalize(
            [SectionKind.Projects, SectionKind.Certifications, SectionKind.Experience, SectionKind.Education],
            educationLeads: true).ShouldBe(
            [SectionKind.Education, SectionKind.Projects, SectionKind.Certifications, SectionKind.Experience]);

    [Fact]
    public void An_order_that_already_leads_with_education_is_returned_unchanged()
    {
        SectionKind[] order = [SectionKind.Summary, SectionKind.Education, SectionKind.Experience];

        SectionOrderPolicy.Normalize(order, educationLeads: true).ShouldBeSameAs(order);
    }

    [Fact]
    public void An_order_is_left_alone_when_education_does_not_lead() =>
        SectionOrderPolicy.Normalize(ExperienceFirst, educationLeads: false).ShouldBeSameAs(ExperienceFirst);

    [Fact]
    public void An_order_with_no_education_section_is_left_alone() =>
        SectionOrderPolicy.Normalize(
            [SectionKind.Summary, SectionKind.Experience], educationLeads: true).ShouldBe(
            [SectionKind.Summary, SectionKind.Experience]);
}
