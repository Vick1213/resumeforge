using System.Text.Json;
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
    public void Every_command_variant_in_the_tailor_commands_schema_is_valid_json()
    {
        var schema = _registry.GetSchema(JsonSchemaRegistry.TailorCommandsSchemaName);
        var oneOf = schema.GetProperty("properties").GetProperty("commands").GetProperty("items").GetProperty("oneOf");

        oneOf.GetArrayLength().ShouldBe(9);
        foreach (var variant in oneOf.EnumerateArray())
        {
            variant.GetProperty("properties").GetProperty("op").GetProperty("const").ValueKind.ShouldBe(JsonValueKind.String);
        }
    }
}
