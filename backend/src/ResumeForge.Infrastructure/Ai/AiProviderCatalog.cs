namespace ResumeForge.Infrastructure.Ai;

/// <summary>The wire format a resolved provider's endpoint speaks.</summary>
public enum AiWireFormat
{
    /// <summary>OpenAI's <c>POST /chat/completions</c> shape — DeepSeek, OpenAI, LM Studio, and any compatible endpoint.</summary>
    OpenAi,

    /// <summary>Anthropic's <c>POST /v1/messages</c> shape.</summary>
    Anthropic,

    /// <summary>No network at all — <see cref="HeuristicLanguageModel"/>.</summary>
    Heuristic,
}

/// <summary>A named default bundle for one <c>ResumeForge:Ai:Provider</c> value (CONTRACTS.md §8).</summary>
public sealed record AiProviderPreset
{
    /// <summary>The provider name, e.g. <c>"deepseek"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The wire format this provider's endpoint speaks.</summary>
    public required AiWireFormat Wire { get; init; }

    /// <summary>Default base address, overridable via <c>ResumeForge:Ai:BaseUrl</c>.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Default model id, overridable via <c>ResumeForge:Ai:Model</c>.</summary>
    public required string Model { get; init; }

    /// <summary>
    /// The environment variable this preset reads its API key from, if any. LM Studio and
    /// the heuristic preset have none.
    /// </summary>
    public string? KeyEnvironmentVariable { get; init; }
}

/// <summary>
/// The frozen preset table from CONTRACTS.md §8, plus <c>auto</c> resolution. Every method
/// here is pure — environment-variable lookup is passed in rather than read directly — so
/// resolution is testable without touching the real process environment.
/// </summary>
public static class AiProviderCatalog
{
    /// <summary>Hosted DeepSeek, OpenAI-compatible wire.</summary>
    public static readonly AiProviderPreset DeepSeek = new()
    {
        Name = "deepseek",
        Wire = AiWireFormat.OpenAi,
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-chat",
        KeyEnvironmentVariable = "DEEPSEEK_API_KEY",
    };

    /// <summary>Hosted OpenAI.</summary>
    public static readonly AiProviderPreset OpenAi = new()
    {
        Name = "openai",
        Wire = AiWireFormat.OpenAi,
        BaseUrl = "https://api.openai.com/v1",
        Model = "gpt-4o",
        KeyEnvironmentVariable = "OPENAI_API_KEY",
    };

    /// <summary>
    /// A local LM Studio server. No key. <see cref="AiProviderPreset.Model"/> is empty
    /// because the default is "whatever model the server currently has loaded" — there is
    /// no sane process-wide default, so an unconfigured <see cref="AiOptions.Model"/> is
    /// sent through as-is and LM Studio uses its loaded model.
    /// </summary>
    public static readonly AiProviderPreset LmStudio = new()
    {
        Name = "lmstudio",
        Wire = AiWireFormat.OpenAi,
        BaseUrl = "http://localhost:1234/v1",
        Model = string.Empty,
        KeyEnvironmentVariable = null,
    };

    /// <summary>Hosted Anthropic.</summary>
    public static readonly AiProviderPreset Anthropic = new()
    {
        Name = "anthropic",
        Wire = AiWireFormat.Anthropic,
        BaseUrl = "https://api.anthropic.com",
        Model = "claude-sonnet-5",
        KeyEnvironmentVariable = "ANTHROPIC_API_KEY",
    };

    /// <summary>No-network fallback. Used by <c>auto</c> when no key is present, and in every test.</summary>
    public static readonly AiProviderPreset Heuristic = new()
    {
        Name = "heuristic",
        Wire = AiWireFormat.Heuristic,
        BaseUrl = string.Empty,
        Model = string.Empty,
        KeyEnvironmentVariable = null,
    };

    private static readonly IReadOnlyDictionary<string, AiProviderPreset> ByName =
        new[] { DeepSeek, OpenAi, LmStudio, Anthropic, Heuristic }
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up the preset registered under <paramref name="name"/> (case-insensitive).</summary>
    public static bool TryGetPreset(string name, out AiProviderPreset preset) =>
        ByName.TryGetValue(name, out preset!);

    /// <summary>
    /// Resolves a configured <c>ResumeForge:Ai:Provider</c> value to a concrete provider
    /// name. Any value other than <c>auto</c> (or unset, which defaults to <c>auto</c>)
    /// passes through unchanged (lowercased) so an explicit selection — including an
    /// unlisted provider name meant to be configured as a generic OpenAI-wire endpoint — is
    /// always honored verbatim.
    ///
    /// <c>auto</c> resolves in this order: <c>DEEPSEEK_API_KEY</c> set → <c>deepseek</c>;
    /// else <c>ANTHROPIC_API_KEY</c> set → <c>anthropic</c>; else <c>heuristic</c>. It never
    /// selects <c>lmstudio</c> — that requires a server the user deliberately started, and
    /// probing the network during DI registration is not acceptable (CONTRACTS.md §8).
    /// </summary>
    public static string ResolveProviderName(string? configuredProvider, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var provider = string.IsNullOrWhiteSpace(configuredProvider) ? "auto" : configuredProvider.Trim().ToLowerInvariant();
        if (!string.Equals(provider, "auto", StringComparison.Ordinal))
        {
            return provider;
        }

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("DEEPSEEK_API_KEY")))
        {
            return DeepSeek.Name;
        }

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            return Anthropic.Name;
        }

        return Heuristic.Name;
    }
}
