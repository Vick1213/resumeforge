using NSubstitute;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Structural tests for <see cref="TailoringGraphFactory"/>: the declared node set and
/// dependency edges must match CONTRACTS.md §7 exactly, so the executor can run the
/// three <c>score-*</c> nodes — and the three <c>verify-*</c>/<c>execute-commands</c>
/// nodes — concurrently.
/// </summary>
public sealed class TailoringGraphFactoryTests
{
    private static ResumeForge.Application.Graph.Graph BuildGraph()
    {
        var factory = new TailoringGraphFactory(
            Substitute.For<IJobRepository>(),
            Substitute.For<IJobAnalyzer>(),
            Substitute.For<IKnowledgeBaseReader>(),
            Substitute.For<IResumeBuilder>(),
            Substitute.For<IRelevanceScorer>(),
            Substitute.For<IBriefBuilder>(),
            Substitute.For<ILanguageModel>(),
            Substitute.For<ICommandValidator>(),
            Substitute.For<IFabricationGuard>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICoverageAnalyzer>(),
            Substitute.For<IResumeRenderer>(),
            new TailorOptions());

        return factory.Create(new TailoringRequest { JobId = "job-1" });
    }

    private static IReadOnlyList<string> DependsOn(ResumeForge.Application.Graph.Graph graph, string name) =>
        graph.Nodes.Single(n => n.Name == name).DependsOn;

    [Fact]
    public void Graph_declares_exactly_the_fourteen_documented_nodes()
    {
        var graph = BuildGraph();

        graph.Nodes.Select(n => n.Name).ShouldBe(
        [
            "fetch-jd", "load-kb", "analyze-jd", "build-base",
            "score-experience", "score-projects", "score-skills",
            "build-brief", "propose-commands", "validate-commands",
            "verify-fabrication", "verify-coverage", "execute-commands", "render",
        ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_three_score_nodes_have_no_edges_between_each_other()
    {
        var graph = BuildGraph();

        var scoreNodeNames = new HashSet<string> { "score-experience", "score-projects", "score-skills" };

        foreach (var name in scoreNodeNames)
        {
            DependsOn(graph, name).ShouldNotContain(other => scoreNodeNames.Contains(other) && other != name);
        }
    }

    [Fact]
    public void Build_brief_depends_on_all_three_score_nodes()
    {
        var graph = BuildGraph();

        DependsOn(graph, "build-brief").ShouldBe(["score-experience", "score-projects", "score-skills"], ignoreOrder: true);
    }

    [Fact]
    public void Verify_fabrication_and_verify_coverage_have_no_edge_between_them()
    {
        var graph = BuildGraph();

        DependsOn(graph, "verify-fabrication").ShouldNotContain("verify-coverage");
        DependsOn(graph, "verify-coverage").ShouldNotContain("verify-fabrication");
    }

    [Fact]
    public void Verify_fabrication_verify_coverage_and_execute_commands_are_independent_siblings_of_validate_commands()
    {
        var graph = BuildGraph();

        DependsOn(graph, "verify-fabrication").ShouldBe(["validate-commands"]);
        DependsOn(graph, "verify-coverage").ShouldBe(["validate-commands"]);
        DependsOn(graph, "execute-commands").ShouldBe(["validate-commands"]);
    }

    [Fact]
    public void Render_depends_on_all_three_downstream_verification_and_execution_nodes()
    {
        var graph = BuildGraph();

        DependsOn(graph, "render").ShouldBe(["verify-fabrication", "verify-coverage", "execute-commands"], ignoreOrder: true);
    }

    [Fact]
    public void Fetch_jd_and_load_kb_are_root_nodes_with_no_dependencies()
    {
        var graph = BuildGraph();

        DependsOn(graph, "fetch-jd").ShouldBeEmpty();
        DependsOn(graph, "load-kb").ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_jd_depends_only_on_fetch_jd()
    {
        var graph = BuildGraph();
        DependsOn(graph, "analyze-jd").ShouldBe(["fetch-jd"]);
    }

    [Fact]
    public void Build_base_depends_only_on_load_kb()
    {
        var graph = BuildGraph();
        DependsOn(graph, "build-base").ShouldBe(["load-kb"]);
    }

    [Fact]
    public void Propose_commands_is_the_only_node_depending_directly_on_build_brief()
    {
        var graph = BuildGraph();

        var dependents = graph.Nodes.Where(n => n.DependsOn.Contains("build-brief")).Select(n => n.Name).ToList();
        dependents.ShouldBe(["propose-commands"]);
    }

    [Fact]
    public void Fetch_jd_and_load_kb_are_marked_critical()
    {
        var graph = BuildGraph();

        graph.Nodes.Single(n => n.Name == "fetch-jd").Critical.ShouldBeTrue();
        graph.Nodes.Single(n => n.Name == "load-kb").Critical.ShouldBeTrue();
    }
}
