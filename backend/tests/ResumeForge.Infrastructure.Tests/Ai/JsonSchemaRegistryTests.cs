using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Ai;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Ai;

/// <summary>Tests for <see cref="JsonSchemaRegistry"/>.</summary>
public sealed class JsonSchemaRegistryTests
{
    private readonly JsonSchemaRegistry _registry = new();

    [Fact]
    public void Contains_the_tailor_commands_schema()
    {
        _registry.TryGetSchema(JsonSchemaRegistry.TailorCommandsSchemaName, out var schema).ShouldBeTrue();
        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("properties").GetProperty("commands").GetProperty("type").GetString().ShouldBe("array");
    }

    [Fact]
    public void Contains_the_field_resolutions_schema()
    {
        _registry.TryGetSchema(JsonSchemaRegistry.FieldResolutionsSchemaName, out var schema).ShouldBeTrue();
        schema.GetProperty("properties").GetProperty("resolutions").GetProperty("type").GetString().ShouldBe("array");
    }

    [Fact]
    public void SchemaNames_lists_both_bundled_schemas()
    {
        _registry.SchemaNames.ShouldContain("tailor-commands");
        _registry.SchemaNames.ShouldContain("field-resolutions");
    }

    [Fact]
    public void GetSchema_throws_for_an_unknown_name()
    {
        Should.Throw<KeyNotFoundException>(() => _registry.GetSchema("does-not-exist"));
    }

    [Fact]
    public void GetPayloadPropertyName_derives_commands_for_tailor_commands()
    {
        _registry.GetPayloadPropertyName(JsonSchemaRegistry.TailorCommandsSchemaName).ShouldBe("commands");
    }

    [Fact]
    public void GetPayloadPropertyName_derives_resolutions_for_field_resolutions()
    {
        _registry.GetPayloadPropertyName(JsonSchemaRegistry.FieldResolutionsSchemaName).ShouldBe("resolutions");
    }

    [Fact]
    public void GetPayloadPropertyName_throws_for_an_unknown_name()
    {
        Should.Throw<KeyNotFoundException>(() => _registry.GetPayloadPropertyName("does-not-exist"));
    }

    /// <summary>
    /// The schema's op list must match <see cref="TailorCommand"/>'s own polymorphic
    /// discriminators exactly. Derived from the attributes rather than pinned to a count, so
    /// adding a command type without teaching the schema about it fails here — the model
    /// would otherwise have no way to emit the new op, and the omission would look like the
    /// model simply choosing not to use it.
    /// </summary>
    [Fact]
    public void Every_command_variant_in_the_tailor_commands_schema_matches_a_declared_command_type()
    {
        var schema = _registry.GetSchema(JsonSchemaRegistry.TailorCommandsSchemaName);
        var oneOf = schema.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("oneOf");

        var schemaOps = oneOf.EnumerateArray()
            .Select(variant => variant.GetProperty("properties").GetProperty("op").GetProperty("const").GetString())
            .ToList();

        var declaredOps = typeof(TailorCommand)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .Select(a => a.TypeDiscriminator as string)
            .ToList();

        schemaOps.ShouldBe(declaredOps, ignoreOrder: true);
    }

    /// <summary>
    /// Guards against exactly the failure a live run hit: <c>setSectionOrder.order</c> must
    /// constrain its elements to precisely the strings the real wire serializer would ever
    /// produce for <see cref="SectionKind"/> — computed here independently of
    /// <see cref="JsonSchemaRegistry"/>'s own derivation, by actually running
    /// <see cref="JsonStringEnumConverter"/> over every <see cref="SectionKind"/> member, so
    /// this fails if a member is ever added without the schema following (whether or not
    /// JsonSchemaRegistry's own derivation logic has a bug).
    /// </summary>
    [Fact]
    public void SetSectionOrder_order_enum_matches_every_SectionKind_wire_value()
    {
        var schema = _registry.GetSchema(JsonSchemaRegistry.TailorCommandsSchemaName);
        var variant = FindVariant(schema, "setSectionOrder");
        var enumElement = variant.GetProperty("properties").GetProperty("order").GetProperty("items").GetProperty("enum");

        var declaredWireValues = enumElement.EnumerateArray().Select(e => e.GetString()).ToList();

        var serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var actualWireValues = Enum.GetValues<SectionKind>()
            .Select(value => JsonSerializer.Serialize(value, serializerOptions).Trim('"'))
            .ToList();

        declaredWireValues.ShouldBe(actualWireValues, ignoreOrder: true);
    }

    /// <summary>
    /// Guards the <c>op</c> discriminator the same way: every value <see cref="TailorCommand"/>
    /// actually accepts (its <see cref="JsonDerivedTypeAttribute"/> declarations) must appear
    /// as exactly one <c>oneOf</c> branch's <c>const</c>, with no branch left over on either
    /// side — so a new command type added to the polymorphic hierarchy without a matching
    /// schema branch (or vice versa) fails this test rather than silently reaching the model
    /// unconstrained.
    /// </summary>
    [Fact]
    public void Op_discriminator_consts_match_every_TailorCommand_JsonDerivedType_exactly()
    {
        var schema = _registry.GetSchema(JsonSchemaRegistry.TailorCommandsSchemaName);
        var oneOf = schema.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("oneOf");

        var declaredOps = oneOf.EnumerateArray()
            .Select(variant => variant.GetProperty("properties").GetProperty("op").GetProperty("const").GetString())
            .ToList();

        var actualOps = typeof(TailorCommand).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (string)a.TypeDiscriminator!)
            .ToList();

        declaredOps.ShouldBe(actualOps, ignoreOrder: true);
    }

    private static JsonElement FindVariant(JsonElement schema, string op)
    {
        var oneOf = schema.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("oneOf");

        foreach (var variant in oneOf.EnumerateArray())
        {
            if (variant.GetProperty("properties").GetProperty("op").GetProperty("const").GetString() == op)
            {
                return variant;
            }
        }

        throw new InvalidOperationException($"No oneOf variant declares op '{op}'.");
    }
}
