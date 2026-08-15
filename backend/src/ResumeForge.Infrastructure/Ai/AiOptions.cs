namespace ResumeForge.Infrastructure.Ai;

/// <summary>Configuration for <see cref="AnthropicLanguageModel"/>, bound from <c>ResumeForge:Ai</c>.</summary>
public sealed class AiOptions
{
    /// <summary>The model id to request, e.g. <c>"claude-sonnet-5"</c>.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// The API key to use when the <c>ANTHROPIC_API_KEY</c> environment variable is not
    /// set. Never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Base address of the Anthropic API.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>The <c>anthropic-version</c> header value.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
