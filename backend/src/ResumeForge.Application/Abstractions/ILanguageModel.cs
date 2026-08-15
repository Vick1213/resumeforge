namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to the language model used to propose tailoring commands and resolve autofill
/// fields. Implementations live in <c>ResumeForge.Infrastructure.Ai</c>: a real client, a
/// no-network heuristic fallback used whenever no API key is configured, and a caching
/// decorator. Application code depends only on this interface.
/// </summary>
public interface ILanguageModel
{
    /// <summary>Identifier of the underlying model, e.g. <c>"claude-sonnet-5"</c>.</summary>
    string ModelId { get; }

    /// <summary>
    /// Completes <paramref name="request"/>, deserializing the response into
    /// <typeparamref name="T"/> under the schema named by <see cref="ModelRequest.SchemaName"/>.
    /// </summary>
    Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct);
}

/// <summary>A single request to the language model port.</summary>
public sealed record ModelRequest
{
    /// <summary>The system prompt.</summary>
    public required string System { get; init; }

    /// <summary>The user-turn content: the compact brief, never the full resume or JD.</summary>
    public required string User { get; init; }

    /// <summary>Names the JSON schema the response must conform to.</summary>
    public required string SchemaName { get; init; }

    /// <summary>Maximum tokens the completion may spend.</summary>
    public int MaxOutputTokens { get; init; } = 1024;

    /// <summary>Sampling temperature.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>When non-null, enables response caching keyed on this value.</summary>
    public string? CacheKey { get; init; }
}

/// <summary>The result of a completed <see cref="ILanguageModel"/> call.</summary>
public sealed record ModelResponse<T>
{
    /// <summary>The deserialized, schema-validated response payload.</summary>
    public required T Value { get; init; }

    /// <summary>Tokens spent producing this response.</summary>
    public required TokenUsage Usage { get; init; }

    /// <summary>True when this response was served from the response cache.</summary>
    public required bool FromCache { get; init; }
}
