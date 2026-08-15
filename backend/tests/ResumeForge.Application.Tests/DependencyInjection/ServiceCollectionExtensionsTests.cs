using Microsoft.Extensions.DependencyInjection;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.DependencyInjection;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.DependencyInjection;

/// <summary>Tests for <see cref="ServiceCollectionExtensions"/>.</summary>
public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// A minimal <see cref="IServiceCollection"/> so these tests can inspect registrations
    /// without depending on the concrete Microsoft.Extensions.DependencyInjection package,
    /// which is not referenced by this project (only the Abstractions package is).
    /// </summary>
    private sealed class TestServiceCollection : List<ServiceDescriptor>, IServiceCollection;

    [Fact]
    public void AddResumeForgeApplication_registers_every_concrete_application_service()
    {
        IServiceCollection services = new TestServiceCollection();

        services.AddResumeForgeApplication();

        AssertRegistered<IGraphExecutor, GraphExecutor>(services);
        AssertRegistered<IJobAnalyzer, JobAnalyzer>(services);
        AssertRegistered<IRelevanceScorer, Bm25RelevanceScorer>(services);
        AssertRegistered<ICommandValidator, CommandValidator>(services);
        AssertRegistered<IFabricationGuard, FabricationGuard>(services);
        AssertRegistered<ICommandExecutor, CommandExecutor>(services);
        AssertRegistered<ICoverageAnalyzer, CoverageAnalyzer>(services);
        AssertRegistered<IResumeBuilder, ResumeBuilder>(services);
        AssertRegistered<IBriefBuilder, BriefBuilder>(services);
        AssertRegistered<ITailoringGraphFactory, TailoringGraphFactory>(services);
        AssertRegistered<ITailoringService, TailoringService>(services);
    }

    [Fact]
    public void AddResumeForgeApplication_registers_options_instances()
    {
        IServiceCollection services = new TestServiceCollection();

        services.AddResumeForgeApplication();

        services.ShouldContain(d => d.ServiceType == typeof(GraphOptions));
        services.ShouldContain(d => d.ServiceType == typeof(ScoringOptions));
        services.ShouldContain(d => d.ServiceType == typeof(TailorOptions));
    }

    [Fact]
    public void AddResumeForgeApplication_applies_the_configure_callback()
    {
        IServiceCollection services = new TestServiceCollection();
        var invoked = false;

        services.AddResumeForgeApplication(options =>
        {
            invoked = true;
            options.Tailor = new TailorOptions { MaxRewrites = 3 };
        });

        invoked.ShouldBeTrue();
        var descriptor = services.Single(d => d.ServiceType == typeof(TailorOptions));
        ((TailorOptions)descriptor.ImplementationInstance!).MaxRewrites.ShouldBe(3);
    }

    [Fact]
    public void AddResumeForgeApplication_does_not_register_infrastructure_only_ports()
    {
        IServiceCollection services = new TestServiceCollection();

        services.AddResumeForgeApplication();

        services.ShouldNotContain(d => d.ServiceType == typeof(Abstractions.ILanguageModel));
        services.ShouldNotContain(d => d.ServiceType == typeof(Abstractions.ISkillTaxonomy));
        services.ShouldNotContain(d => d.ServiceType == typeof(Abstractions.IKnowledgeBaseReader));
    }

    [Fact]
    public void AddResumeForgeApplication_returns_the_same_service_collection_for_chaining()
    {
        IServiceCollection services = new TestServiceCollection();

        var returned = services.AddResumeForgeApplication();

        returned.ShouldBeSameAs(services);
    }

    private static void AssertRegistered<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = services.Where(d => d.ServiceType == typeof(TService)).ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(TImplementation));
    }
}
