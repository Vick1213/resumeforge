using ResumeForge.Application.Abstractions;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// Fully resolved language-model configuration, bound from <c>ResumeForge:Ai</c> and the
/// CONTRACTS.md §8 preset table by <c>ServiceCollectionExtensions.AddAi</c>. By the time any
/// consumer sees this instance, <see cref="Provider"/> already names a concrete provider
/// (never <c>auto</c>), and every other field already carries that provider's preset
/// default unless overridden by an explicit <c>ResumeForge:Ai:*</c> config value.
/// </summary>
public sealed class AiOptions
{
    /// <summary>
    /// The resolved provider name, e.g. <c>"deepseek"</c>, <c>"anthropic"</c>,
    /// <c>"lmstudio"</c>. Feeds <see cref="ILanguageModel.ModelId"/> together with
    /// <see cref="Model"/> so switching providers never serves a cached response generated
    /// by a different model (CONTRACTS.md §8).
    /// </summary>
    public string Provider { get; set; } = "heuristic";

    /// <summary>The model id to request, e.g. <c>"claude-sonnet-5"</c> or <c>"deepseek-chat"</c>.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The resolved API key, if any. Never logged. Omitted entirely from outgoing requests
    /// when null or empty (LM Studio accepts none).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Base address of the provider's API.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The <c>anthropic-version</c> header value. Used only by the <c>anthropic</c> preset.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// <c>auto</c> (the default) to probe <see cref="OpenAiCompatibleLanguageModel"/>'s
    /// structured-output strategy ladder and remember the winner, or an explicit pin —
    /// <c>tool</c>, <c>json_schema</c>, or <c>prompted</c> — that skips probing entirely.
    /// Used only by <see cref="OpenAiCompatibleLanguageModel"/>.
    /// </summary>
    public string StructuredOutput { get; set; } = "auto";
}
