namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// Thrown when a provider stops generating because it hit the request's output-token cap
/// (<c>finish_reason: "length"</c> on the OpenAI-compatible wire, <c>stop_reason:
/// "max_tokens"</c> on Anthropic's) rather than because the model finished.
/// </summary>
/// <remarks>
/// Distinct from schema-validation failure on purpose. A truncated response is *usually*
/// unparseable JSON, so without this check it surfaces as a confusing
/// <c>JsonReaderException</c> ("reached end of data") that sends you looking for a malformed
/// model rather than a cap that is simply too low. It is also not worth retrying: the same
/// request with the same cap truncates again, so this bypasses the retry/fallback ladder and
/// fails immediately with the number the caller needs to raise.
/// </remarks>
public sealed class ModelResponseTruncatedException : Exception
{
    /// <summary>Creates an exception describing a response cut short by the output-token cap.</summary>
    public ModelResponseTruncatedException(string schemaName, int maxOutputTokens, int completionTokens)
        : base($"Model stopped at the output-token cap while emitting schema '{schemaName}': " +
               $"MaxOutputTokens was {maxOutputTokens} and the completion used {completionTokens}, " +
               "so the response is truncated and cannot be parsed. Raise the request's MaxOutputTokens.")
    {
        SchemaName = schemaName;
        MaxOutputTokens = maxOutputTokens;
        CompletionTokens = completionTokens;
    }

    /// <summary>The schema the truncated response was meant to satisfy.</summary>
    public string SchemaName { get; }

    /// <summary>The cap that was hit.</summary>
    public int MaxOutputTokens { get; }

    /// <summary>Completion tokens the provider reported, or 0 when it reported none.</summary>
    public int CompletionTokens { get; }
}
