using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ResumeForge.Application.Abstractions;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// <see cref="ILanguageModel"/> backed by the real Anthropic Messages API. Forces
/// structured output via tool-use: a single tool named <c>emit_result</c> is declared with
/// an <c>input_schema</c> taken from <see cref="JsonSchemaRegistry"/> and <c>tool_choice</c>
/// pinned to it, so the model's entire reply is the validated JSON payload. When the
/// payload fails to deserialize into the caller's requested type, the failure (and the
/// caller's C# <c>required</c> members and polymorphic discriminator) is treated as a
/// schema-validation failure and retried once, feeding the error back to the model.
/// </summary>
public sealed class AnthropicLanguageModel(
    IHttpClientFactory httpClientFactory,
    AiOptions options,
    JsonSchemaRegistry schemaRegistry,
    ILogger<AnthropicLanguageModel> logger) : ILanguageModel
{
    /// <summary>The name registered for this model's <see cref="IHttpClientFactory"/> client.</summary>
    public const string HttpClientName = "anthropic";

    private const string ToolName = "emit_result";
    private const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public string ModelId => options.Model;

    /// <inheritdoc />
    public async Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No Anthropic API key is configured. Set the ANTHROPIC_API_KEY environment variable or 'ResumeForge:Ai:ApiKey'.");
        }

        var schema = schemaRegistry.GetSchema(request.SchemaName);
        var client = httpClientFactory.CreateClient(HttpClientName);

        string? validationError = null;
        Exception? lastFailure = null;
        var totalUsage = TokenUsage.Empty;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var (toolInputJson, usage) = await SendAsync(client, apiKey, ModelId, request, schema, validationError, ct).ConfigureAwait(false);
            totalUsage += usage;

            try
            {
                var value = JsonSerializer.Deserialize<T>(toolInputJson, SerializerOptions)
                    ?? throw new JsonException("Model response deserialized to null.");

                return new ModelResponse<T> { Value = value, Usage = totalUsage, FromCache = false };
            }
            catch (JsonException ex)
            {
                lastFailure = ex;
                validationError = ex.Message;
                logger.LogWarning(
                    "Model response for schema '{SchemaName}' failed validation on attempt {Attempt}/{MaxAttempts}: {Error}",
                    request.SchemaName, attempt, MaxAttempts, ex.Message);
            }
        }

        throw new InvalidOperationException(
            $"Model response for schema '{request.SchemaName}' failed schema validation after {MaxAttempts} attempt(s).", lastFailure);
    }

    private static async Task<(string ToolInputJson, TokenUsage Usage)> SendAsync(
        HttpClient client, string apiKey, string modelId, ModelRequest request, JsonElement schema, string? previousError, CancellationToken ct)
    {
        var userContent = previousError is null
            ? request.User
            : $"{request.User}\n\nYour previous response failed schema validation: {previousError}\nRespond again with corrected tool input that satisfies the schema exactly.";

        var payload = new JsonObject
        {
            ["model"] = modelId,
            ["max_tokens"] = request.MaxOutputTokens,
            ["temperature"] = request.Temperature,
            ["system"] = request.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = ToolName,
                ["description"] = $"Emits the '{request.SchemaName}' result as validated structured data.",
                ["input_schema"] = JsonNode.Parse(schema.GetRawText()),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = ToolName },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var response = await client.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new AnthropicApiException((int)response.StatusCode, responseBody);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var usage = new TokenUsage
        {
            InputTokens = root.TryGetProperty("usage", out var usageEl) && usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
            OutputTokens = root.TryGetProperty("usage", out var usageEl2) && usageEl2.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
            ModelCalls = 1,
            CacheHits = 0,
        };

        if (root.TryGetProperty("content", out var contentEl))
        {
            foreach (var block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_use" &&
                    block.TryGetProperty("name", out var nameEl) && nameEl.GetString() == ToolName &&
                    block.TryGetProperty("input", out var inputEl))
                {
                    return (inputEl.GetRawText(), usage);
                }
            }
        }

        throw new InvalidOperationException("Anthropic response did not include the expected tool_use content block.");
    }

    private string? ResolveApiKey()
    {
        var envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return !string.IsNullOrWhiteSpace(envKey) ? envKey : options.ApiKey;
    }
}
