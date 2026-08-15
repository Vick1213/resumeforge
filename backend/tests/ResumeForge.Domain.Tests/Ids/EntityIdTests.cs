using System.Text.Json;
using ResumeForge.Domain.Ids;
using Shouldly;
using Xunit;

namespace ResumeForge.Domain.Tests.Ids;

/// <summary>
/// Tests for <see cref="EntityId"/> parsing, formatting, and JSON round-tripping.
/// </summary>
public sealed class EntityIdTests
{
    [Theory]
    [InlineData("exp:acme-corp", EntityKind.Experience, "acme-corp", null, null)]
    [InlineData("exp:acme-corp#2", EntityKind.Experience, "acme-corp", 2, null)]
    [InlineData("prj:graph-runner", EntityKind.Project, "graph-runner", null, null)]
    [InlineData("prj:graph-runner#0", EntityKind.Project, "graph-runner", 0, null)]
    [InlineData("edu:uw-madison", EntityKind.Education, "uw-madison", null, null)]
    [InlineData("skl:languages", EntityKind.SkillGroup, "languages", null, null)]
    [InlineData("skl:languages#csharp", EntityKind.SkillGroup, "languages", null, "csharp")]
    [InlineData("cert:az-204", EntityKind.Certification, "az-204", null, null)]
    public void Parse_documented_forms_round_trip(string text, EntityKind kind, string slug, int? ordinal, string? subKey)
    {
        var id = EntityId.Parse(text);

        id.Kind.ShouldBe(kind);
        id.Slug.ShouldBe(slug);
        id.Ordinal.ShouldBe(ordinal);
        id.SubKey.ShouldBe(subKey);
        id.ToString().ShouldBe(text);
        EntityId.Parse(id.ToString()).ShouldBe(id);
    }

    [Fact]
    public void Parse_summary_has_no_slug()
    {
        var id = EntityId.Parse("sum");

        id.Kind.ShouldBe(EntityKind.Summary);
        id.Slug.ShouldBe(string.Empty);
        id.Ordinal.ShouldBeNull();
        id.SubKey.ShouldBeNull();
        id.ToString().ShouldBe("sum");
    }

    [Theory]
    [InlineData("")]
    [InlineData("exp")]
    [InlineData("exp:")]
    [InlineData("exp:Acme-Corp")]
    [InlineData("exp:acme_corp")]
    [InlineData("exp:acme-corp#")]
    [InlineData("exp:acme-corp#-1")]
    [InlineData("xyz:acme-corp")]
    [InlineData("sum:foo")]
    [InlineData("sum#0")]
    [InlineData(":acme-corp")]
    public void Parse_rejects_invalid_input(string text)
    {
        Should.Throw<FormatException>(() => EntityId.Parse(text));
        EntityId.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_null_throws_format_exception()
    {
        Should.Throw<FormatException>(() => EntityId.Parse(null!));
    }

    [Fact]
    public void TryParse_valid_input_returns_true()
    {
        EntityId.TryParse("exp:acme-corp#3", out var id).ShouldBeTrue();
        id.Ordinal.ShouldBe(3);
    }

    [Fact]
    public void Child_with_ordinal_produces_numeric_fragment()
    {
        var parent = EntityId.Parse("exp:acme-corp");
        var child = parent.Child(2);

        child.ToString().ShouldBe("exp:acme-corp#2");
        child.Ordinal.ShouldBe(2);
        child.SubKey.ShouldBeNull();
    }

    [Fact]
    public void Child_with_subkey_produces_named_fragment()
    {
        var parent = EntityId.Parse("skl:languages");
        var child = parent.Child("csharp");

        child.ToString().ShouldBe("skl:languages#csharp");
        child.SubKey.ShouldBe("csharp");
        child.Ordinal.ShouldBeNull();
    }

    [Fact]
    public void Child_negative_ordinal_throws()
    {
        var parent = EntityId.Parse("exp:acme-corp");
        Should.Throw<ArgumentOutOfRangeException>(() => parent.Child(-1));
    }

    [Fact]
    public void Child_on_summary_throws()
    {
        var summary = EntityId.Parse("sum");
        Should.Throw<InvalidOperationException>(() => summary.Child(0));
    }

    [Fact]
    public void Parent_strips_fragment()
    {
        var bullet = EntityId.Parse("exp:acme-corp#2");
        bullet.Parent.ToString().ShouldBe("exp:acme-corp");
    }

    [Fact]
    public void Parent_of_already_parent_id_is_itself()
    {
        var entry = EntityId.Parse("exp:acme-corp");
        entry.Parent.ShouldBe(entry);
    }

    [Fact]
    public void Json_round_trips_through_system_text_json()
    {
        var id = EntityId.Parse("skl:languages#csharp");

        var json = JsonSerializer.Serialize(id);
        json.ShouldBe("\"skl:languages#csharp\"");

        var deserialized = JsonSerializer.Deserialize<EntityId>(json);
        deserialized.ShouldBe(id);
    }

    [Fact]
    public void Json_deserialize_of_invalid_string_throws_json_exception()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EntityId>("\"not-a-valid-id\""));
    }

    [Fact]
    public void Equality_is_based_on_kind_slug_and_fragment()
    {
        EntityId.Parse("exp:acme-corp#0").ShouldBe(EntityId.Parse("exp:acme-corp#0"));
        EntityId.Parse("exp:acme-corp#0").ShouldNotBe(EntityId.Parse("exp:acme-corp#1"));
        EntityId.Parse("exp:acme-corp").ShouldNotBe(EntityId.Parse("prj:acme-corp"));
    }
}
