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
    /// renderers, job posting fetcher, GitHub importer, and language model stack — resolving
    /// <c>ResumeForge:Ai:Provider</c> to a concrete provider (falling back to
    /// <see cref="HeuristicLanguageModel"/>, which needs no key, when <c>auto</c> finds
    /// neither <c>DEEPSEEK_API_KEY</c> nor <c>ANTHROPIC_API_KEY</c>) per CONTRACTS.md §8.
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

    /// <summary>
    /// Resolves the <c>ResumeForge:Ai</c> provider preset (CONTRACTS.md §8) and wires up
    /// every <see cref="ILanguageModel"/> implementation. All three cores
    /// (<see cref="AnthropicLanguageModel"/>, <see cref="OpenAiCompatibleLanguageModel"/>,
    /// <see cref="HeuristicLanguageModel"/>) are always registered — only the resolved
    /// preset's wire format decides which one the <see cref="ILanguageModel"/> factory
    /// actually constructs, so the other two never touch the network or the filesystem.
    /// </summary>
    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        var aiSection = configuration.GetSection("ResumeForge:Ai");
        string? Get(string key) => aiSection[key] is { Length: > 0 } value ? value : null;

        var providerName = AiProviderCatalog.ResolveProviderName(Get("Provider"), Environment.GetEnvironmentVariable);
        if (!AiProviderCatalog.TryGetPreset(providerName, out var preset))
        {
            // An unlisted provider name (Together, Groq, Ollama, vLLM, OpenRouter, ...) is
            // treated as a generic OpenAI-wire endpoint per CONTRACTS.md §8 ("an unlisted
            // provider needs config only, never a code change") — BaseUrl/Model/ApiKey must
            // all be supplied via ResumeForge:Ai:* config in that case.
            preset = new AiProviderPreset { Name = providerName, Wire = AiWireFormat.OpenAi, BaseUrl = string.Empty, Model = string.Empty, KeyEnvironmentVariable = null };
        }

        var apiKey = Get("ApiKey") ?? (preset.KeyEnvironmentVariable is { } envVar ? Environment.GetEnvironmentVariable(envVar) : null);

        var aiOptions = new AiOptions
        {
            Provider = preset.Name,
            Model = Get("Model") ?? preset.Model,
            ApiKey = apiKey,
            BaseUrl = Get("BaseUrl") ?? preset.BaseUrl,
            AnthropicVersion = Get("AnthropicVersion") ?? "2023-06-01",
            StructuredOutput = Get("StructuredOutput") ?? "auto",
        };

        services.TryAddSingleton(aiOptions);
        services.TryAddSingleton<JsonSchemaRegistry>();
        services.TryAddSingleton<StructuredOutputStrategyMemo>();

        services.AddHttpClient(AnthropicLanguageModel.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(aiOptions.BaseUrl is { Length: > 0 } url ? url : AiProviderCatalog.Anthropic.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("anthropic-version", aiOptions.AnthropicVersion);
        });

        services.AddHttpClient(OpenAiCompatibleLanguageModel.HttpClientName, client =>
        {
            // HttpClient only appends a relative request URI ("chat/completions") after
            // the base address's last path segment when that base address ends in '/' —
            // otherwise it replaces the segment (dropping "/v1" from the openai/lmstudio
            // presets entirely). Falls back to the openai preset's URL when unset (e.g. the
            // resolved provider is heuristic, so this client is registered but never used)
            // so building it never throws.
            var baseUrl = aiOptions.BaseUrl is { Length: > 0 } url ? url : AiProviderCatalog.OpenAi.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.TryAddScoped<HeuristicLanguageModel>();
        services.TryAddSingleton<AnthropicLanguageModel>();
        services.TryAddSingleton<OpenAiCompatibleLanguageModel>();

        services.TryAddScoped<ILanguageModel>(sp =>
        {
            ILanguageModel selected = preset.Wire switch
            {
                AiWireFormat.Anthropic => sp.GetRequiredService<AnthropicLanguageModel>(),
                AiWireFormat.OpenAi => sp.GetRequiredService<OpenAiCompatibleLanguageModel>(),
                _ => sp.GetRequiredService<HeuristicLanguageModel>(),
            };

            return new CachingLanguageModel(selected, sp.GetRequiredService<ResumeForgeDbContext>(), sp.GetRequiredService<TimeProvider>());
        });
    }
}
