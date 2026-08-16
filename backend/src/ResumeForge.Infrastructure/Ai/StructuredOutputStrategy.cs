namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// The three structured-output strategies <see cref="OpenAiCompatibleLanguageModel"/>
/// degrades through, in order, per CONTRACTS.md §8.
/// </summary>
public enum StructuredOutputStrategy
{
    /// <summary>A single forced tool call named <c>emit_result</c>, pinned via <c>tool_choice</c>.</summary>
    ForcedToolCall,

    /// <summary><c>response_format: {"type": "json_schema", ...}</c>, carrying the same schema.</summary>
    JsonSchema,

    /// <summary><c>response_format: {"type": "json_object"}</c> with the schema inlined into the system prompt.</summary>
    PromptedJson,
}

/// <summary>
/// Remembers which <see cref="StructuredOutputStrategy"/> a provider accepted, for the
/// lifetime of the process (CONTRACTS.md §8): a provider that rejects a strategy is probed
/// once, not once per request. Registered as a singleton so every
/// <see cref="OpenAiCompatibleLanguageModel"/> call shares the same memory; each test
/// constructs its own instance so probes never leak between tests.
/// </summary>
public sealed class StructuredOutputStrategyMemo
{
    private StructuredOutputStrategy? _resolved;

    /// <summary>The strategy a previous call proved works against the configured provider, if any.</summary>
    public StructuredOutputStrategy? Resolved => _resolved;

    /// <summary>Records <paramref name="strategy"/> as the working strategy for the rest of the process.</summary>
    public void Remember(StructuredOutputStrategy strategy) => _resolved = strategy;
}
