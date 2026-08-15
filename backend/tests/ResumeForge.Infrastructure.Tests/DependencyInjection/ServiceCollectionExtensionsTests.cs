using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.DependencyInjection;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Verifies <see cref="ServiceCollectionExtensions.AddResumeForgeInfrastructure"/> builds a
/// container where every Application port resolves to a working Infrastructure
/// implementation, and that the language model selection picks the heuristic model when no
/// API key is configured.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ResumeForge"] = "Data Source=:memory:",
        };
        configure?.Invoke(settings);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddResumeForgeInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Every_declared_application_port_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IKnowledgeBaseReader>().ShouldNotBeNull();
        services.GetRequiredService<IKnowledgeBaseWriter>().ShouldNotBeNull();
        services.GetRequiredService<ISkillTaxonomy>().ShouldNotBeNull();
        services.GetRequiredService<IResumeRepository>().ShouldNotBeNull();
        services.GetRequiredService<IJobRepository>().ShouldNotBeNull();
        services.GetRequiredService<ITailoringRunRepository>().ShouldNotBeNull();
        services.GetRequiredService<ILearnedFieldMapRepository>().ShouldNotBeNull();
        services.GetRequiredService<IApplicationRepository>().ShouldNotBeNull();
        services.GetRequiredService<IResumeRenderer>().ShouldNotBeNull();
        services.GetRequiredService<IJobPostingFetcher>().ShouldNotBeNull();
        services.GetRequiredService<ILanguageModel>().ShouldNotBeNull();
    }

    [Fact]
    public void ISkillTaxonomy_is_registered_as_a_singleton()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        ReferenceEquals(
            scopeA.ServiceProvider.GetRequiredService<ISkillTaxonomy>(),
            scopeB.ServiceProvider.GetRequiredService<ISkillTaxonomy>()).ShouldBeTrue();
    }

    [Fact]
    public void ILanguageModel_wraps_the_heuristic_model_when_no_api_key_is_configured()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

        try
        {
            using var provider = BuildProvider();
            using var scope = provider.CreateScope();

            var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

            languageModel.ShouldBeOfType<CachingLanguageModel>();
            languageModel.ModelId.ShouldBe("heuristic-v1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }

    [Fact]
    public void ILanguageModel_wraps_the_anthropic_model_when_an_api_key_is_configured()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key-value");

        try
        {
            using var provider = BuildProvider();
            using var scope = provider.CreateScope();

            var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

            languageModel.ShouldBeOfType<CachingLanguageModel>();
            languageModel.ModelId.ShouldBe("claude-sonnet-5");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }
}
