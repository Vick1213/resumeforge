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
// See EnvironmentVariableCollection: this class sets ANTHROPIC_API_KEY process-wide, so it
// must not run beside anything else that reads it.
[Collection(EnvironmentVariableCollection.Name)]
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
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", null), ("DEEPSEEK_API_KEY", null));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

        languageModel.ShouldBeOfType<CachingLanguageModel>();
        languageModel.ModelId.ShouldBe("heuristic-v1");
    }

    [Fact]
    public void ILanguageModel_wraps_the_anthropic_model_when_an_anthropic_api_key_is_configured()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", "test-key-value"), ("DEEPSEEK_API_KEY", null));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

        languageModel.ShouldBeOfType<CachingLanguageModel>();
        languageModel.ModelId.ShouldBe("anthropic/claude-sonnet-5");
    }

    [Fact]
    public void ILanguageModel_wraps_the_deepseek_model_when_a_deepseek_api_key_is_configured()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", null), ("DEEPSEEK_API_KEY", "sk-deepseek"));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

        languageModel.ShouldBeOfType<CachingLanguageModel>();
        languageModel.ModelId.ShouldBe("deepseek/deepseek-chat");
    }

    [Fact]
    public void Auto_prefers_deepseek_over_anthropic_when_both_keys_are_present()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", "sk-ant"), ("DEEPSEEK_API_KEY", "sk-deepseek"));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILanguageModel>().ModelId.ShouldBe("deepseek/deepseek-chat");
    }

    [Fact]
    public void Auto_never_selects_lmstudio_even_with_no_keys_configured()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", null), ("DEEPSEEK_API_KEY", null));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Falls all the way to heuristic — never lmstudio, which requires a server the user
        // deliberately started (CONTRACTS.md §8).
        scope.ServiceProvider.GetRequiredService<ILanguageModel>().ModelId.ShouldBe("heuristic-v1");
    }

    [Fact]
    public void An_explicit_lmstudio_provider_is_honored_and_carries_the_configured_model_in_its_id()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", null), ("DEEPSEEK_API_KEY", null));

        using var provider = BuildProvider(settings =>
        {
            settings["ResumeForge:Ai:Provider"] = "lmstudio";
            settings["ResumeForge:Ai:Model"] = "qwen3-8b";
        });
        using var scope = provider.CreateScope();

        var languageModel = scope.ServiceProvider.GetRequiredService<ILanguageModel>();

        languageModel.ModelId.ShouldBe("lmstudio/qwen3-8b");
    }

    [Fact]
    public void An_explicit_provider_selection_overrides_auto_even_with_no_matching_key()
    {
        using var env = new ScopedEnvironmentVariables(("ANTHROPIC_API_KEY", null), ("DEEPSEEK_API_KEY", null));

        using var provider = BuildProvider(settings => settings["ResumeForge:Ai:Provider"] = "anthropic");
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILanguageModel>().ModelId.ShouldBe("anthropic/claude-sonnet-5");
    }

    /// <summary>Sets environment variables for the lifetime of the instance, restoring the previous values on dispose.</summary>
    private sealed class ScopedEnvironmentVariables : IDisposable
    {
        private readonly (string Name, string? Previous)[] _previous;

        public ScopedEnvironmentVariables(params (string Name, string? Value)[] values)
        {
            _previous = values.Select(v => (v.Name, Environment.GetEnvironmentVariable(v.Name))).ToArray();
            foreach (var (name, value) in values)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _previous)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
