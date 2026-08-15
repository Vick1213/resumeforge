using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeForge.Domain.Ids;

/// <summary>
/// Serializes an <see cref="EntityId"/> to and from its canonical string form
/// (e.g. <c>"exp:acme-corp#0"</c>).
/// </summary>
public sealed class EntityIdJsonConverter : JsonConverter<EntityId>
{
    /// <inheritdoc />
    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
        {
            throw new JsonException("Expected a string value for EntityId.");
        }

        try
        {
            return EntityId.Parse(text);
        }
        catch (FormatException ex)
        {
            throw new JsonException($"Invalid EntityId '{text}'.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
