using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Tests.TestSupport;

namespace ResumeForge.Infrastructure.Tests.Rendering;

/// <summary>Shared fixture document for renderer tests: a mix of included and excluded entries.</summary>
internal static class RenderingTestData
{
    public static ResumeDocument Document(IReadOnlyList<SectionKind>? sectionOrder = null)
    {
        var includedExperience = TestData.Experience(
            "exp:acme", "Senior Engineer", "Acme Corp", new DateOnly(2022, 1, 1), null,
            bullets: [TestData.Bullet("exp:acme#0", "Cut checkout latency 8x.")], included: true);

        var excludedExperience = TestData.Experience(
            "exp:old", "Intern", "OldCo", new DateOnly(2018, 1, 1), new DateOnly(2018, 6, 1),
            bullets: [TestData.Bullet("exp:old#0", "Should never appear in output.")], included: false);

        var includedSkills = TestData.SkillGroup(
            "skl:languages", "Languages", [TestData.Skill("skl:languages#csharp", "C#", "csharp", emphasized: true)]);

        var excludedSkills = TestData.SkillGroup(
            "skl:soft", "Soft Skills", [TestData.Skill("skl:soft#leadership", "Leadership", "leadership")], included: false);

        return TestData.Document(
            basics: TestData.Basics("Jordan Rivera", headline: "Senior Backend Engineer", email: "jordan@example.com"),
            experience: [includedExperience, excludedExperience],
            skills: [includedSkills, excludedSkills],
            summary: "A backend engineer with distributed systems experience.",
            sectionOrder: sectionOrder ??
            [
                SectionKind.Summary, SectionKind.Skills, SectionKind.Experience,
                SectionKind.Projects, SectionKind.Education, SectionKind.Certifications,
            ]);
    }
}
