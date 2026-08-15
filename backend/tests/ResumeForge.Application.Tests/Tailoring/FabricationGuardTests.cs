using ResumeForge.Application.Tailoring;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>Tests for <see cref="FabricationGuard"/> — the anti-fabrication check.</summary>
public sealed class FabricationGuardTests
{
    private readonly FabricationGuard _guard = new();

    [Fact]
    public void Rewrite_retaining_the_same_metric_passes()
    {
        var passed = _guard.IsSafe(
            "Cut p99 checkout latency from 840ms to 120ms by rebuilding the fan-out.",
            "Rebuilt the checkout fan-out, cutting p99 latency to 120ms.",
            out var reason);

        passed.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void Number_reformatted_with_commas_is_not_treated_as_new()
    {
        var passed = _guard.IsSafe(
            "Processed 1200 transactions per second at peak load.",
            "Scaled the pipeline to handle 1,200 transactions per second.",
            out var reason);

        passed.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void Case_difference_in_an_identifier_like_p99_is_not_treated_as_new()
    {
        var passed = _guard.IsSafe(
            "Reduced p99 latency significantly.",
            "Drove down P99 latency across the service.",
            out var reason);

        passed.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void Currency_reformatting_is_not_treated_as_new()
    {
        var passed = _guard.IsSafe(
            "Saved the company $1.2M annually through automation.",
            "Delivered 1.2M in annual savings through automation.",
            out var reason);

        passed.ShouldBeTrue();
    }

    [Fact]
    public void Rewrite_that_drops_all_metrics_fails()
    {
        var passed = _guard.IsSafe(
            "Cut p99 latency from 840ms to 120ms, a 3x improvement.",
            "Improved checkout latency substantially through architectural changes.",
            out var reason);

        passed.ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Fact]
    public void Rewrite_that_invents_a_new_percentage_fails()
    {
        var passed = _guard.IsSafe(
            "Improved deployment reliability across the team.",
            "Reduced infrastructure costs by 60% through automation.",
            out var reason);

        passed.ShouldBeFalse();
        reason!.ShouldContain("60%");
    }

    [Fact]
    public void Rewrite_that_invents_a_new_number_fails_even_when_a_different_original_number_is_retained()
    {
        var passed = _guard.IsSafe(
            "Led a team of 5 engineers to ship the platform.",
            "Led a team of 5 engineers, reducing costs by 60%.",
            out var reason);

        passed.ShouldBeFalse();
        reason!.ShouldContain("60%");
    }

    [Fact]
    public void Rewrite_retaining_a_proper_noun_passes_when_original_has_no_numbers()
    {
        var passed = _guard.IsSafe(
            "Migrated the platform from Postgres to Kubernetes.",
            "Led the migration from Postgres to a Kubernetes-based platform.",
            out var reason);

        passed.ShouldBeTrue();
    }

    [Fact]
    public void Rewrite_dropping_the_only_proper_noun_fails_when_original_has_no_numbers()
    {
        var passed = _guard.IsSafe(
            "Migrated services onto Kubernetes for the platform team.",
            "Migrated services onto a modern container orchestration platform.",
            out var reason);

        passed.ShouldBeFalse();
    }

    [Fact]
    public void Multiplier_token_3x_is_recognized_and_retained()
    {
        var passed = _guard.IsSafe(
            "Sped up the build pipeline by 3x.",
            "Cut CI pipeline wall-clock time by 3x through parallelization.",
            out var reason);

        passed.ShouldBeTrue();
    }

    [Fact]
    public void Original_with_no_numbers_or_proper_nouns_allows_any_rewrite_without_new_numbers()
    {
        var passed = _guard.IsSafe(
            "Wrote clear documentation for the onboarding process.",
            "Authored onboarding documentation used by every new hire.",
            out var reason);

        passed.ShouldBeTrue();
    }

    [Fact]
    public void Rewrite_that_both_retains_evidence_and_adds_a_new_number_still_fails()
    {
        var passed = _guard.IsSafe(
            "Migrated the platform to Kubernetes.",
            "Migrated the platform to Kubernetes, a 3x scalability improvement.",
            out var reason);

        passed.ShouldBeFalse();
        reason!.ShouldContain("3x");
    }
}
