using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Scoring;
using ResumeForge.Application.Tailoring;

namespace ResumeForge.Application.DependencyInjection;

/// <summary>Registers the Application layer's own services. Never registers Infrastructure ports' implementations.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds every concrete Application-layer service against its port. Callers (typically
    /// the API host) are still responsible for registering the Infrastructure
    /// implementations of every port declared in <c>ResumeForge.Application.Abstractions</c>
    /// (<see cref="Abstractions.ILanguageModel"/>, <see cref="Abstractions.ISkillTaxonomy"/>,
    /// the knowledge-base and repository ports, etc.), plus logging and a
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AddResumeForgeApplication(
        this IServiceCollection services, Action<ApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ApplicationOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options.Graph);
        services.TryAddSingleton(options.Scoring);
        services.TryAddSingleton(options.Tailor);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IGraphExecutor, GraphExecutor>();
        services.TryAddSingleton<IJobAnalyzer, JobAnalyzer>();
        services.TryAddSingleton<IRelevanceScorer, Bm25RelevanceScorer>();
        services.TryAddSingleton<ICommandValidator, CommandValidator>();
        services.TryAddSingleton<IFabricationGuard, FabricationGuard>();
        services.TryAddSingleton<ICommandExecutor, CommandExecutor>();
        services.TryAddSingleton<ICoverageAnalyzer, CoverageAnalyzer>();
        services.TryAddSingleton<IResumeBuilder, ResumeBuilder>();
        services.TryAddSingleton<IBriefBuilder, BriefBuilder>();
        services.TryAddSingleton<ITailoringGraphFactory, TailoringGraphFactory>();
        services.TryAddScoped<ITailoringService, TailoringService>();

        return services;
    }
}
