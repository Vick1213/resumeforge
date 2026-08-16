using ResumeForge.Application.Analysis;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Analysis;

/// <summary>
/// Tests for <see cref="SeniorityClassifier"/>, shared between job-posting titles
/// (<see cref="JobAnalyzer"/>) and resume experience-entry roles
/// (<see cref="ResumeForge.Application.Scoring.Bm25RelevanceScorer"/>).
/// </summary>
public sealed class SeniorityClassifierTests
{
    [Theory]
    [InlineData("Senior Software Engineer", SeniorityLevel.Senior)]
    [InlineData("Staff Engineer", SeniorityLevel.Staff)]
    [InlineData("Principal Engineer", SeniorityLevel.Principal)]
    [InlineData("Software Engineer II", SeniorityLevel.Mid)]
    [InlineData("Software Engineering Intern", SeniorityLevel.Intern)]
    [InlineData("Junior Developer", SeniorityLevel.Junior)]
    [InlineData("Backend Engineer", SeniorityLevel.Unknown)]
    [InlineData(null, SeniorityLevel.Unknown)]
    [InlineData("", SeniorityLevel.Unknown)]
    public void Classifies_a_title_by_keyword_cue(string? title, SeniorityLevel expected)
    {
        SeniorityClassifier.FromTitle(title).ShouldBe(expected);
    }

    [Fact]
    public void Classification_is_not_hardcoded_to_the_word_intern()
    {
        // Any recognizable seniority cue must classify, not just "intern" — this is what
        // lets the level-fit penalty in Bm25RelevanceScorer generalize instead of special
        // casing internships.
        SeniorityClassifier.FromTitle("Summer Co-op").ShouldBe(SeniorityLevel.Unknown);
        SeniorityClassifier.FromTitle("Lead Platform Engineer").ShouldBe(SeniorityLevel.Senior);
    }
}
