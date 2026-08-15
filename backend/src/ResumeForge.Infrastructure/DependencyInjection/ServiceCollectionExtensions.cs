using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuestPDF.Infrastructure;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.GitHub;
using ResumeForge.Infrastructure.Jobs;
using ResumeForge.Infrastructure.Knowledge;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Rendering;
using ResumeForge.Infrastructure.Skills;

namespace ResumeForge.Infrastructure.DependencyInjection;

/// <summary>Registers every Infrastructure-layer implementation of the Application ports, plus supporting services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the markdown knowledge base, skill taxonomy, EF/SQLite persistence, resume
    /// renderers, job posting fetcher, GitHub importer, and language model stack (picking
    /// <see cref="HeuristicLanguageModel"/> automatically when no Anthropic API key is
    /// configured, per CONTRACTS.md §8).
    /// </summary>
    public static IServiceCollection AddResumeForgeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // QuestPDF requires a license declaration once per process; PdfResumeRenderer's
        // static constructor also guards this for callers who construct it directly.
        QuestPDF.Settings.License = LicenseType.Community;

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new TailorOptions());

        // A real host (WebApplicationBuilder / Host.CreateApplicationBuilder) already
        // registers IConfiguration itself; TryAdd keeps this a no-op there while still
        // making IProfileRootProvider resolvable for callers that build a bare
        // IServiceCollection (e.g. tests).
        services.TryAddSingleton(configuration);

        AddKnowledgeBase(services);
        AddSkills(services);
        AddPersistence(services, configuration);
        AddRendering(services);
        AddJobs(services);
        AddGitHub(services);
        AddAi(services, configuration);

        return services;
    }

    private static void AddKnowledgeBase(IServiceCollection services)
    {
        services.TryAddSingleton<IProfileRootProvider, ProfileRootProvider>();
        services.TryAddScoped<IKnowledgeBaseReader, MarkdownKnowledgeBaseReader>();
        services.TryAddScoped<IKnowledgeBaseWriter, MarkdownKnowledgeBaseWriter>();
    }

    private static void AddSkills(IServiceCollection services)
    {
        services.TryAddSingleton<ISkillTaxonomy, SkillTaxonomy>();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:ResumeForge"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Data Source=resumeforge.db";
        }

        services.AddDbContext<ResumeForgeDbContext>(options => options.UseSqlite(connectionString));

        services.TryAddScoped<IResumeRepository, ResumeRepository>();
        services.TryAddScoped<IJobRepository, JobRepository>();
        services.TryAddScoped<ITailoringRunRepository, TailoringRunRepository>();
        services.TryAddScoped<ILearnedFieldMapRepository, LearnedFieldMapRepository>();
        services.TryAddScoped<IApplicationRepository, ApplicationRepository>();
    }

    private static void AddRendering(IServiceCollection services)
    {
        services.TryAddSingleton<MarkdownResumeRenderer>();
        services.TryAddSingleton<HtmlResumeRenderer>();
        services.TryAddSingleton<PdfResumeRenderer>();
        services.TryAddSingleton<IResumeRenderer, ResumeRenderer>();
    }

    private static void AddJobs(IServiceCollection services)
    {
        services.AddHttpClient(JobPostingFetcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ResumeForge/1.0");
        });

        services.TryAddScoped<IJobPostingFetcher, JobPostingFetcher>();
    }

    private static void AddGitHub(IServiceCollection services)
    {
        services.AddHttpClient(GitHubProjectImporter.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ResumeForge/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        });

        services.TryAddScoped<IGitHubProjectImporter, GitHubProjectImporter>();
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        var aiSection = configuration.GetSection("ResumeForge:Ai");
        var aiOptions = new AiOptions
        {
            Model = aiSection["Model"] is { Length: > 0 } model ? model : "claude-sonnet-5",
            ApiKey = aiSection["ApiKey"],
            BaseUrl = aiSection["BaseUrl"] is { Length: > 0 } baseUrl ? baseUrl : "https://api.anthropic.com",
            AnthropicVersion = aiSection["AnthropicVersion"] is { Length: > 0 } version ? version : "2023-06-01",
        };

        services.TryAddSingleton(aiOptions);
        services.TryAddSingleton<JsonSchemaRegistry>();

        services.AddHttpClient(AnthropicLanguageModel.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(aiOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("anthropic-version", aiOptions.AnthropicVersion);
        });

        services.TryAddSingleton<HeuristicLanguageModel>();
        services.TryAddSingleton<AnthropicLanguageModel>();

        var hasApiKey =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) ||
            !string.IsNullOrWhiteSpace(aiOptions.ApiKey);

        services.TryAddScoped<ILanguageModel>(sp =>
        {
            ILanguageModel selected = hasApiKey
                ? sp.GetRequiredService<AnthropicLanguageModel>()
                : sp.GetRequiredService<HeuristicLanguageModel>();

            return new CachingLanguageModel(selected, sp.GetRequiredService<ResumeForgeDbContext>(), sp.GetRequiredService<TimeProvider>());
        });
    }
}
