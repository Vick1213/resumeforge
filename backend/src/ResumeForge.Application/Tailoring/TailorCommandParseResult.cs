using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// The result of deserializing one element of a model-proposed command array. Exists so a
/// single malformed command cannot take the whole <c>propose-commands</c> node down with it
/// (CONTRACTS.md §6: commands are rejected, never allowed to abort the run). Closed to
/// exactly two shapes, the same way <see cref="TailorCommand"/> itself is closed by
/// <see cref="JsonPolymorphicAttribute"/>.
/// </summary>
public abstract record TailorCommandParseResult
{
    private TailorCommandParseResult()
    {
    }

    /// <summary>An array element that deserialized into a well-formed <see cref="TailorCommand"/>.</summary>
    public sealed record Parsed(TailorCommand Command) : TailorCommandParseResult;

    /// <summary>
    /// An array element that failed to deserialize as any known <see cref="TailorCommand"/>
    /// shape. There is no <see cref="TailorCommand"/> instance to carry — the model's output
    /// never parsed into one — so this keeps only <see cref="RawJson"/> (the element exactly
    /// as sent, kept so a cached response can round-trip back to this same state rather than
    /// silently turning into whatever the raw text happens to parse as on replay) and
    /// <see cref="Error"/> (why it failed). <see cref="CommandValidator"/> turns this into a
    /// <c>malformed-command</c> <see cref="RejectedCommand"/> with a null <c>Command</c> —
    /// never a fabricated stand-in that could be mistaken for something the model actually
    /// sent.
    /// </summary>
    public sealed record Malformed(int Index, string RawJson, string Error) : TailorCommandParseResult;
}

/// <summary>
/// A command list deserialized element-by-element instead of all at once, via
/// <see cref="TailorCommandParseResultListConverter"/>. <see cref="TailoringGraphFactory"/>
/// requests this type — rather than <c>IReadOnlyList&lt;TailorCommand&gt;</c> — as the
/// <c>T</c> it asks <see cref="Abstractions.ILanguageModel.CompleteAsync{T}"/> for, so that a
/// single element the model malformed becomes a <see cref="TailorCommandParseResult.Malformed"/>
/// entry rather than an exception that discards every other, perfectly good, command. Every
/// <c>ILanguageModel</c> implementation needs zero changes to support this: none of them know
/// anything about commands specifically, they just deserialize whatever <c>T</c> the caller
/// asks for, and this type carries its own deserialization behaviour via the
/// <see cref="JsonConverterAttribute"/> below — the same mechanism <see cref="TailorCommand"/>
/// itself already uses for its polymorphic discriminator.
/// </summary>
[JsonConverter(typeof(TailorCommandParseResultListConverter))]
public sealed class TailorCommandParseResultList : IReadOnlyList<TailorCommandParseResult>
{
    private readonly IReadOnlyList<TailorCommandParseResult> _results;

    /// <summary>Wraps an already-computed list of per-element parse results.</summary>
    public TailorCommandParseResultList(IReadOnlyList<TailorCommandParseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        _results = results;
    }

    /// <inheritdoc />
    public int Count => _results.Count;

    /// <inheritdoc />
    public TailorCommandParseResult this[int index] => _results[index];

    /// <inheritdoc />
    public IEnumerator<TailorCommandParseResult> GetEnumerator() => _results.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Reads a JSON array into a <see cref="TailorCommandParseResultList"/> one element at a time,
/// catching a per-element deserialization failure instead of letting it abort the whole array
/// (CONTRACTS.md §6). Two cases are deliberately excluded from that leniency and still throw a
/// <see cref="JsonException"/> straight through — exactly as a bare
/// <c>JsonSerializer.Deserialize&lt;IReadOnlyList&lt;TailorCommand&gt;&gt;</c> call would —
/// so they keep going through the caller's existing retry/fallback ladder: the payload not
/// being a JSON array at all, and every single element failing to parse. Both are a real
/// provider problem, not a single bad command among otherwise-good ones.
/// </summary>
public sealed class TailorCommandParseResultListConverter : JsonConverter<TailorCommandParseResultList>
{
    /// <inheritdoc />
    public override TailorCommandParseResultList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Expected a JSON array of commands, found {root.ValueKind}.");
        }

        var results = new List<TailorCommandParseResult>();
        var index = 0;
        foreach (var element in root.EnumerateArray())
        {
            results.Add(ParseElement(index, element, options));
            index++;
        }

        if (results.Count > 0 && results.TrueForAll(r => r is TailorCommandParseResult.Malformed))
        {
            var reasons = string.Join(" | ", results
                .Cast<TailorCommandParseResult.Malformed>()
                .Select(m => $"[{m.Index}] {m.Error}"));
            throw new JsonException(
                $"Every one of {results.Count} proposed command(s) failed to deserialize: {reasons}");
        }

        return new TailorCommandParseResultList(results);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TailorCommandParseResultList value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartArray();

        foreach (var result in value)
        {
            switch (result)
            {
                case TailorCommandParseResult.Parsed parsed:
                    JsonSerializer.Serialize(writer, parsed.Command, options);
                    break;

                case TailorCommandParseResult.Malformed malformed:
                    // Written back exactly as received, so a cached response replays to this
                    // same Malformed state rather than to whatever its raw text happens to
                    // parse as by the time it's read back.
                    using (var raw = JsonDocument.Parse(malformed.RawJson))
                    {
                        raw.RootElement.WriteTo(writer);
                    }

                    break;
            }
        }

        writer.WriteEndArray();
    }

    private static TailorCommandParseResult ParseElement(int index, JsonElement element, JsonSerializerOptions options)
    {
        var rawJson = element.GetRawText();

        try
        {
            var command = element.Deserialize<TailorCommand>(options)
                ?? throw new JsonException("Command element deserialized to null.");
            return new TailorCommandParseResult.Parsed(command);
        }
        catch (JsonException ex)
        {
            return new TailorCommandParseResult.Malformed(index, rawJson, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            // System.Text.Json's polymorphic machinery throws NotSupportedException (not
            // JsonException) for some malformed-discriminator shapes; either way this one
            // element is malformed, not a reason to fail the whole batch.
            return new TailorCommandParseResult.Malformed(index, rawJson, ex.Message);
        }
    }
}
