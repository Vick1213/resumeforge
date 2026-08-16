using System.Text.Json;
using ResumeForge.Application.Tailoring;
using Shouldly;
using Xunit;

namespace ResumeForge.Application.Tests.Tailoring;

/// <summary>
/// Tests for <see cref="TailorCommandParseResultListConverter"/>: the JSON conversion that
/// lets <see cref="TailoringGraphFactory"/> deserialize a model-proposed command array one
/// element at a time, so a single malformed command never takes every other, perfectly good,
/// command down with it (CONTRACTS.md §6).
/// </summary>
public sealed class TailorCommandParseResultTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void A_well_formed_element_deserializes_to_Parsed()
    {
        var results = Deserialize("""[{"op":"setSummary","text":"A concise summary."}]""");

        var parsed = results.Single().ShouldBeOfType<TailorCommandParseResult.Parsed>();
        parsed.Command.ShouldBeOfType<SetSummaryCommand>().Text.ShouldBe("A concise summary.");
    }

    [Fact]
    public void One_malformed_element_among_good_ones_becomes_Malformed_without_losing_the_rest()
    {
        // The exact shape of the live failure: sixteen good commands, then a setSectionOrder
        // whose order names something that is not a legal SectionKind.
        var json = """
            [
                {"op":"include","targets":["exp:acme"]},
                {"op":"setSectionOrder","order":["engineering","skills","experience","projects","education","certifications"]},
                {"op":"setSummary","text":"Tailored summary."}
            ]
            """;

        var results = Deserialize(json);

        results.Count.ShouldBe(3);
        results[0].ShouldBeOfType<TailorCommandParseResult.Parsed>().Command.ShouldBeOfType<IncludeCommand>();

        var malformed = results[1].ShouldBeOfType<TailorCommandParseResult.Malformed>();
        malformed.Index.ShouldBe(1);
        malformed.Error.ShouldNotBeNullOrWhiteSpace();
        malformed.RawJson.ShouldContain("engineering");

        results[2].ShouldBeOfType<TailorCommandParseResult.Parsed>().Command.ShouldBeOfType<SetSummaryCommand>();
    }

    [Fact]
    public void An_unrecognized_op_discriminator_becomes_Malformed()
    {
        // Paired with a well-formed sibling so this doesn't itself trip the "every element
        // failed" guard — that case is covered separately below.
        var results = Deserialize("""[{"op":"doSomethingUnknown","foo":"bar"},{"op":"setSummary","text":"ok"}]""");

        results[0].ShouldBeOfType<TailorCommandParseResult.Malformed>();
        results[1].ShouldBeOfType<TailorCommandParseResult.Parsed>();
    }

    [Fact]
    public void A_missing_required_field_becomes_Malformed()
    {
        // "include" requires "targets"; omitting it is exactly the shape of malformation a
        // provider might produce under a schema it only partially honors.
        var results = Deserialize("""[{"op":"include"},{"op":"setSummary","text":"ok"}]""");

        results[0].ShouldBeOfType<TailorCommandParseResult.Malformed>();
        results[1].ShouldBeOfType<TailorCommandParseResult.Parsed>();
    }

    [Fact]
    public void An_empty_array_deserializes_to_an_empty_list_without_throwing()
    {
        // Zero commands is a legitimate response (the model had nothing to propose), not a
        // provider failure — must not trip the "every element failed" guard below.
        var results = Deserialize("[]");

        results.ShouldBeEmpty();
    }

    [Fact]
    public void Every_element_failing_to_parse_throws_a_JsonException()
    {
        var json = """[{"op":"nope"},{"op":"alsoNope"}]""";

        Should.Throw<JsonException>(() => Deserialize(json));
    }

    [Fact]
    public void A_payload_that_is_not_an_array_throws_a_JsonException()
    {
        Should.Throw<JsonException>(() => Deserialize("""{"op":"include","targets":["exp:acme"]}"""));
    }

    [Fact]
    public void Round_trips_Parsed_and_Malformed_elements_through_serialize_then_deserialize()
    {
        // CachingLanguageModel serializes ModelResponse<T>.Value to persist it, then later
        // deserializes the cached text back into the same T — a cached response containing a
        // malformed element must replay to that same Malformed state, not to whatever its raw
        // text happens to parse as on the way back in.
        var original = Deserialize("""
            [
                {"op":"setSummary","text":"Tailored summary."},
                {"op":"setSectionOrder","order":["engineering"]}
            ]
            """);

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = Deserialize(json);

        roundTripped.Count.ShouldBe(2);
        roundTripped[0].ShouldBeOfType<TailorCommandParseResult.Parsed>().Command.ShouldBeOfType<SetSummaryCommand>()
            .Text.ShouldBe("Tailored summary.");
        roundTripped[1].ShouldBeOfType<TailorCommandParseResult.Malformed>();
    }

    private static TailorCommandParseResultList Deserialize(string json) =>
        JsonSerializer.Deserialize<TailorCommandParseResultList>(json, Options)!;
}
