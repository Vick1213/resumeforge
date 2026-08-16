using ResumeForge.Application.Tailoring;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="ModelEffortExtensions"/> — the CONTRACTS.md §6 effort table.</summary>
public sealed class ModelEffortExtensionsTests
{
    [Theory]
    [InlineData(ModelEffort.Minimal, 0)]
    [InlineData(ModelEffort.Standard, 6)]
    [InlineData(ModelEffort.Thorough, 12)]
    [InlineData(ModelEffort.Maximum, 20)]
    public void MaxRewrites_matches_the_contracts_table(ModelEffort effort, int expected)
    {
        effort.MaxRewrites().ShouldBe(expected);
    }

    [Fact]
    public void ResolveMaxRewrites_uses_the_effort_preset_when_no_override_is_given()
    {
        ModelEffort.Thorough.ResolveMaxRewrites(null).ShouldBe(12);
    }

    [Fact]
    public void ResolveMaxRewrites_explicit_override_wins_over_the_effort_preset()
    {
        // Effort is a preset, not a lock (CONTRACTS.md §6): an explicit MaxRewrites always wins.
        ModelEffort.Minimal.ResolveMaxRewrites(9).ShouldBe(9);
        ModelEffort.Maximum.ResolveMaxRewrites(0).ShouldBe(0);
    }
}
