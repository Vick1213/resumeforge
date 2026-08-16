using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Tailoring;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="AnthropicLanguageModel"/>: <c>ModelId</c>, the <c>x-api-key</c>
/// header, the retry-with-fed-back-error behaviour on a schema mismatch, and — the point of
/// this file existing at all — that the client unwraps the schema's object wrapper
/// (<c>{"commands": [...]}</c>, <c>{"resolutions": [...]}</c>) the way a real Anthropic
/// <c>tool_use.input</c> actually arrives, rather than expecting a bare array. Every HTTP call
/// goes through <see cref="StubHttpMessageHandler"/> — never a real network call.
/// </summary>
// Serialized with the other class that mutates provider API-key environment variables.
// Those variables are process-global, so running the two classes in parallel makes this
// one read a key another test set, and the failure is timing-dependent.
[Collection(EnvironmentVariableCollection.Name)]
public sealed class AnthropicLanguageModelTests
{
    private static readonly JsonSchemaRegistry SchemaRegistry = new();

    [Fact]
    public void ModelId_carries_provider_and_model()
    {
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request => ToolUseResponse("""{"commands":[]}"""));

        model.ModelId.ShouldBe("anthropic/claude-sonnet-5");
    }

    [Fact]
    public async Task Sends_the_x_api_key_header_from_options_when_no_environment_variable_is_set()
    {
        HttpRequestMessage? captured = null;
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
        {
            captured = request;
            return ToolUseResponse("""{"commands":[]}""");
        });

        await model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Headers.TryGetValues("x-api-key", out var values).ShouldBeTrue();
        values!.ShouldContain("sk-ant-test");
    }

    [Fact]
    public async Task Throws_when_no_api_key_is_configured()
    {
        // ResolveApiKey prefers ANTHROPIC_API_KEY from the environment over options.ApiKey;
        // clear it for the duration of this test so the assertion holds regardless of the
        // host process's own environment.
        var previousEnvKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        try
        {
            var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = null }, request => ToolUseResponse("""{"commands":[]}"""));

            await Should.ThrowAsync<InvalidOperationException>(() => model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousEnvKey);
        }
    }

    [Fact]
    public async Task Deserializes_tailor_commands_from_the_schema_wrapped_tool_use_input()
    {
        // This is the shape Anthropic actually sends: tool_use.input is the whole
        // input_schema's object, {"commands": [...]} — not a bare array. AnthropicLanguageModel
        // must unwrap it the same way OpenAiCompatibleLanguageModel does.
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
            ToolUseResponse("""
                {"commands":[
                    {"op":"include","targets":["exp:acme"],"rationale":"Best match."},
                    {"op":"setSummary","text":"A concise summary."}
                ]}
                """));

        var response = await model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None);

        response.Value.Count.ShouldBe(2);
        var include = response.Value[0].ShouldBeOfType<IncludeCommand>();
        include.Targets.ShouldBe(["exp:acme"]);
        response.Value[1].ShouldBeOfType<SetSummaryCommand>().Text.ShouldBe("A concise summary.");
    }

    [Fact]
    public async Task Deserializes_a_command_list_with_one_malformed_element_without_throwing()
    {
        // Proves the seam this fix chose: AnthropicLanguageModel needed zero changes to
        // support per-element command tolerance. It just deserializes whatever T the caller
        // asks for — here TailorCommandParseResultList — which carries its own lenient
        // conversion behaviour via [JsonConverter], the same mechanism TailorCommand itself
        // already uses for its own polymorphic discriminator.
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
            ToolUseResponse("""
                {"commands":[
                    {"op":"include","targets":["exp:acme"]},
                    {"op":"setSectionOrder","order":["engineering","skills","experience","projects","education","certifications"]},
                    {"op":"setSummary","text":"Tailored summary."}
                ]}
                """));

        var response = await model.CompleteAsync<TailorCommandParseResultList>(NewTailorCommandsRequest(), CancellationToken.None);

        response.Value.Count.ShouldBe(3);
        response.Value[0].ShouldBeOfType<TailorCommandParseResult.Parsed>();
        response.Value[1].ShouldBeOfType<TailorCommandParseResult.Malformed>();
        response.Value[2].ShouldBeOfType<TailorCommandParseResult.Parsed>();
    }

    [Fact]
    public async Task A_command_list_where_every_element_is_malformed_still_fails_through_the_retry_ladder()
    {
        // Zero good commands out of several is a real provider problem, not a single bad
        // command among otherwise-good ones — it must still exhaust the retry/fallback
        // ladder exactly as a bare schema mismatch always has.
        var attempts = 0;
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
        {
            attempts++;
            return ToolUseResponse("""{"commands":[{"op":"nope"},{"op":"alsoNope"}]}""");
        });

        await Should.ThrowAsync<InvalidOperationException>(
            () => model.CompleteAsync<TailorCommandParseResultList>(NewTailorCommandsRequest(), CancellationToken.None));

        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Deserializes_field_resolutions_from_the_schema_wrapped_tool_use_input()
    {
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
            ToolUseResponse("""
                {"resolutions":[
                    {"elementId":"el-1","canonicalKey":"email","confidence":0.9,"optionValue":null},
                    {"elementId":"el-2","canonicalKey":"","confidence":0.0}
                ]}
                """));

        var response = await model.CompleteAsync<IReadOnlyList<FieldResolutionPayload>>(NewFieldResolutionsRequest(), CancellationToken.None);

        response.Value.Count.ShouldBe(2);
        response.Value[0].ElementId.ShouldBe("el-1");
        response.Value[0].CanonicalKey.ShouldBe("email");
        response.Value[0].Confidence.ShouldBe(0.9);
        response.Value[1].CanonicalKey.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Retries_once_on_a_schema_mismatch_and_feeds_the_error_back()
    {
        var requestBodies = new List<string>();
        var attempt = 0;
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
        {
            requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            attempt++;
            return attempt == 1 ? ToolUseResponse("""{"unrelated":"shape"}""") : ToolUseResponse("""{"commands":[]}""");
        });

        var response = await model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None);

        response.Value.Count.ShouldBe(0);
        requestBodies.Count.ShouldBe(2);
        requestBodies[1].ShouldContain("failed schema validation");
    }

    [Fact]
    public async Task Throws_after_a_second_schema_mismatch()
    {
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request =>
            ToolUseResponse("""{"unrelated":"shape"}"""));

        await Should.ThrowAsync<InvalidOperationException>(() => model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Maps_usage_and_cache_read_input_tokens()
    {
        var model = NewModel(new AiOptions { Provider = "anthropic", Model = "claude-sonnet-5", ApiKey = "sk-ant-test" }, request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"content":[{"type":"tool_use","name":"emit_result","input":{"commands":[]}}],
                 "usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":80}}
                """),
        });

        var response = await model.CompleteAsync<IReadOnlyList<TailorCommand>>(NewTailorCommandsRequest(), CancellationToken.None);

        response.Usage.InputTokens.ShouldBe(100);
        response.Usage.OutputTokens.ShouldBe(20);
        response.Usage.CacheHits.ShouldBe(80);
    }

    private static AnthropicLanguageModel NewModel(AiOptions options, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHttpMessageHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://fake.example.test/") };
        return new AnthropicLanguageModel(new StubHttpClientFactory(client), options, SchemaRegistry, NullLogger<AnthropicLanguageModel>.Instance);
    }

    private static ModelRequest NewTailorCommandsRequest() => new()
    {
        System = "You propose tailoring commands.",
        User = "some brief",
        SchemaName = JsonSchemaRegistry.TailorCommandsSchemaName,
    };

    private static ModelRequest NewFieldResolutionsRequest() => new()
    {
        System = "You resolve web form fields to canonical autofill keys.",
        User = "some brief",
        SchemaName = JsonSchemaRegistry.FieldResolutionsSchemaName,
    };

    private static HttpResponseMessage ToolUseResponse(string inputJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"emit_result\",\"input\":" + inputJson +
            "}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}"),
    };

    /// <summary>
    /// Wire shape matching the <c>"field-resolutions"</c> JSON schema
    /// (<see cref="JsonSchemaRegistry"/>), local to this test file since Infrastructure has no
    /// reference to the Api layer's own <c>FieldResolution</c> contract type.
    /// </summary>
    private sealed record FieldResolutionPayload
    {
        public required string ElementId { get; init; }

        public required string CanonicalKey { get; init; }

        public required double Confidence { get; init; }

        public string? OptionValue { get; init; }
    }
}
