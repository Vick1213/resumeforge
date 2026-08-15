namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Accumulated token spend and model-call accounting for a unit of work — a single model
/// call, a single graph node, or an entire tailoring run. Instances combine with
/// <see cref="operator +"/> so callers can fold per-node usage into a run total.
/// </summary>
public sealed record TokenUsage
{
    /// <summary>Tokens consumed by the prompt (system + user + schema).</summary>
    public required int InputTokens { get; init; }

    /// <summary>Tokens consumed by the completion.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Number of model calls this usage represents.</summary>
    public required int ModelCalls { get; init; }

    /// <summary>Number of those calls that were served from the response cache.</summary>
    public required int CacheHits { get; init; }

    /// <summary>The zero value: no tokens, no calls, no cache hits.</summary>
    public static TokenUsage Empty { get; } = new()
    {
        InputTokens = 0,
        OutputTokens = 0,
        ModelCalls = 0,
        CacheHits = 0,
    };

    /// <summary>Component-wise sum of two usages.</summary>
    public static TokenUsage operator +(TokenUsage left, TokenUsage right) => new()
    {
        InputTokens = left.InputTokens + right.InputTokens,
        OutputTokens = left.OutputTokens + right.OutputTokens,
        ModelCalls = left.ModelCalls + right.ModelCalls,
        CacheHits = left.CacheHits + right.CacheHits,
    };
}
