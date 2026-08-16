using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>
/// Tests for <see cref="OpenAiCompatibleLanguageModel"/>: the structured-output strategy
/// ladder, its per-process memoization, provider-qualified <c>ModelId</c>, auth-header
/// omission with no key, and usage/cache-hit mapping. Every HTTP call goes through
/// <see cref="StubHttpMessageHandler"/> — never a real network call.
/// </summary>
public sealed class OpenAiCompatibleLanguageModelTests
{
    private static readonly JsonSchemaRegistry SchemaRegistry = new();

    [Fact]
    public void ModelId_carries_provider_and_model()
    {
        var model = NewModel(new AiOptions { Provider = "lmstudio", Model = "qwen3-8b" }, request => ForcedToolCallResponse());

        model.ModelId.ShouldBe("lmstudio/qwen3-8b");
    }

    [Fact]
    public async Task Omits_the_authorization_header_when_no_api_key_is_configured()
    {
        HttpRequestMessage? captured = null;
        var model = NewModel(new AiOptions { Provider = "lmstudio", Model = "qwen3-8b", ApiKey = null }, request =>
        {
            captured = request;
            return ForcedToolCallResponse();
        });

        await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Sends_a_bearer_token_when_an_api_key_is_configured()
    {
        HttpRequestMessage? captured = null;
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat", ApiKey = "sk-test" }, request =>
        {
            captured = request;
            return ForcedToolCallResponse();
        });

        await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        captured!.Headers.Authorization.ShouldNotBeNull();
        captured.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        captured.Headers.Authorization.Parameter.ShouldBe("sk-test");
    }

    [Fact]
    public async Task Uses_the_forced_tool_call_strategy_by_default_and_deserializes_the_arguments()
    {
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat" }, request => ForcedToolCallResponse());

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Value.Foo.ShouldBe("bar");
        response.Usage.InputTokens.ShouldBe(10);
        response.Usage.OutputTokens.ShouldBe(5);
        response.Usage.ModelCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Falls_through_to_json_schema_when_the_provider_returns_http_400_for_the_forced_tool_call()
    {
        var calls = new List<StrategyKind>();
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat" }, request =>
        {
            var kind = ClassifyStrategy(request);
            calls.Add(kind);
            return kind == StrategyKind.ForcedToolCall
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"tool_choice not supported"}""") }
                : JsonSchemaResponse();
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Value.Foo.ShouldBe("bar");
        calls.ShouldBe([StrategyKind.ForcedToolCall, StrategyKind.JsonSchema]);
    }

    [Fact]
    public async Task Falls_through_to_json_schema_when_the_provider_returns_200_with_no_tool_call()
    {
        // deepseek-reasoner's behavior per CONTRACTS.md §8: HTTP 200, but no tool_calls.
        var calls = new List<StrategyKind>();
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-reasoner" }, request =>
        {
            var kind = ClassifyStrategy(request);
            calls.Add(kind);
            return kind == StrategyKind.ForcedToolCall ? NoToolCallResponse() : JsonSchemaResponse();
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Value.Foo.ShouldBe("bar");
        calls.ShouldBe([StrategyKind.ForcedToolCall, StrategyKind.JsonSchema]);
    }

    [Fact]
    public async Task Falls_all_the_way_through_to_prompted_json_when_both_earlier_strategies_are_rejected()
    {
        var calls = new List<StrategyKind>();
        var model = NewModel(new AiOptions { Provider = "openai", Model = "gpt-4o" }, request =>
        {
            var kind = ClassifyStrategy(request);
            calls.Add(kind);
            return kind == StrategyKind.PromptedJson
                ? PromptedJsonResponse()
                : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"unsupported"}""") };
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Value.Foo.ShouldBe("bar");
        calls.ShouldBe([StrategyKind.ForcedToolCall, StrategyKind.JsonSchema, StrategyKind.PromptedJson]);
    }

    [Fact]
    public async Task Remembers_the_winning_strategy_for_the_next_call()
    {
        var calls = new List<StrategyKind>();
        var memo = new StructuredOutputStrategyMemo();
        var options = new AiOptions { Provider = "deepseek", Model = "deepseek-chat" };

        HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var kind = ClassifyStrategy(request);
            calls.Add(kind);
            return kind == StrategyKind.ForcedToolCall
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") }
                : JsonSchemaResponse();
        }

        var first = NewModel(options, Respond, memo);
        await first.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);
        calls.ShouldBe([StrategyKind.ForcedToolCall, StrategyKind.JsonSchema]);

        calls.Clear();
        var second = NewModel(options, Respond, memo);
        await second.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        // The second call skips straight to the remembered strategy — no probing.
        calls.ShouldBe([StrategyKind.JsonSchema]);
    }

    [Fact]
    public async Task Retries_once_on_a_schema_mismatch_and_feeds_the_error_back()
    {
        var requestBodies = new List<string>();
        var attempt = 0;
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat" }, request =>
        {
            requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            attempt++;
            return attempt == 1 ? ToolCallResponseWithArguments("""{"unrelated":"shape"}""") : ForcedToolCallResponse();
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Value.Foo.ShouldBe("bar");
        requestBodies.Count.ShouldBe(2);
        requestBodies[1].ShouldContain("failed schema validation");
    }

    [Fact]
    public async Task Throws_after_a_second_schema_mismatch_on_the_same_strategy()
    {
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat" }, request =>
            ToolCallResponseWithArguments("""{"unrelated":"shape"}"""));

        await Should.ThrowAsync<InvalidOperationException>(() => model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Maps_deepseeks_prompt_cache_hit_tokens_onto_cache_hits()
    {
        var model = NewModel(new AiOptions { Provider = "deepseek", Model = "deepseek-chat" }, request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"choices":[{"message":{"tool_calls":[{"function":{"name":"emit_result","arguments":"{\"foo\":\"bar\"}"}}]}}],
                 "usage":{"prompt_tokens":100,"completion_tokens":20,"prompt_cache_hit_tokens":80}}
                """),
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Usage.CacheHits.ShouldBe(80);
    }

    [Fact]
    public async Task Maps_openais_prompt_tokens_details_cached_tokens_onto_cache_hits()
    {
        var model = NewModel(new AiOptions { Provider = "openai", Model = "gpt-4o" }, request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"choices":[{"message":{"tool_calls":[{"function":{"name":"emit_result","arguments":"{\"foo\":\"bar\"}"}}]}}],
                 "usage":{"prompt_tokens":100,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":64}}}
                """),
        });

        var response = await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        response.Usage.CacheHits.ShouldBe(64);
    }

    [Fact]
    public async Task A_pinned_structured_output_strategy_skips_the_ladder_entirely()
    {
        var calls = new List<StrategyKind>();
        var model = NewModel(
            new AiOptions { Provider = "lmstudio", Model = "qwen3-8b", StructuredOutput = "json_schema" },
            request =>
            {
                calls.Add(ClassifyStrategy(request));
                return JsonSchemaResponse();
            });

        await model.CompleteAsync<TestPayload>(NewRequest(), CancellationToken.None);

        calls.ShouldBe([StrategyKind.JsonSchema]);
    }

    private static OpenAiCompatibleLanguageModel NewModel(
        AiOptions options, Func<HttpRequestMessage, HttpResponseMessage> respond, StructuredOutputStrategyMemo? memo = null)
    {
        var handler = new StubHttpMessageHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://fake.example.test/v1/") };
        return new OpenAiCompatibleLanguageModel(
            new StubHttpClientFactory(client), options, SchemaRegistry, memo ?? new StructuredOutputStrategyMemo(),
            NullLogger<OpenAiCompatibleLanguageModel>.Instance);
    }

    private static ModelRequest NewRequest() => new()
    {
        System = "You resolve web form fields to canonical autofill keys.",
        User = "some brief",
        SchemaName = JsonSchemaRegistry.FieldResolutionsSchemaName,
    };

    private enum StrategyKind { ForcedToolCall, JsonSchema, PromptedJson, Unknown }

    private static StrategyKind ClassifyStrategy(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        var json = JsonNode.Parse(body)!.AsObject();

        if (json.ContainsKey("tool_choice"))
        {
            return StrategyKind.ForcedToolCall;
        }

        var responseFormatType = json["response_format"]?["type"]?.GetValue<string>();
        return responseFormatType switch
        {
            "json_schema" => StrategyKind.JsonSchema,
            "json_object" => StrategyKind.PromptedJson,
            _ => StrategyKind.Unknown,
        };
    }

    private static HttpResponseMessage ForcedToolCallResponse() => ToolCallResponseWithArguments("""{"foo":"bar"}""");

    private static HttpResponseMessage ToolCallResponseWithArguments(string argumentsJson)
    {
        var escaped = JsonSerializer.Serialize(argumentsJson);
        var body = "{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"emit_result\",\"arguments\":" +
            escaped + "}}]}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private static HttpResponseMessage NoToolCallResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
            {"choices":[{"message":{"content":"I cannot call tools."}}],
             "usage":{"prompt_tokens":8,"completion_tokens":3}}
            """),
    };

    private static HttpResponseMessage JsonSchemaResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
            {"choices":[{"message":{"content":"{\"foo\":\"bar\"}"}}],
             "usage":{"prompt_tokens":10,"completion_tokens":5}}
            """),
    };

    private static HttpResponseMessage PromptedJsonResponse() => JsonSchemaResponse();

    private sealed record TestPayload
    {
        public required string Foo { get; init; }
    }
}
