using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>
/// Builds the <see cref="ValueConverter{TModel,TProvider}"/> and
/// <see cref="ValueComparer{T}"/> pair every JSON-backed column in
/// <see cref="ResumeForgeDbContext"/> uses, against one explicit, stable
/// <see cref="JsonSerializerOptions"/> (camelCase, the same string enum converter as the
/// API). Equality and hashing compare the serialized JSON rather than CLR reference or
/// record equality, because the domain records this stores hold their own child
/// collections behind reference-typed properties (e.g. <c>ExperienceEntry.Bullets</c>),
/// whose default equality is reference-based — without this, EF change tracking would
/// never detect a modified collection.
/// </summary>
internal static class JsonColumn
{
    /// <summary>The options every JSON column is serialized and deserialized with.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Builds a converter that stores <typeparamref name="T"/> as a JSON string column.</summary>
    public static ValueConverter<T, string> Converter<T>() => new(
        value => JsonSerializer.Serialize(value, Options),
        json => JsonSerializer.Deserialize<T>(json, Options)!);

    /// <summary>
    /// Builds a structural-equality comparer for a JSON-backed collection (or other
    /// reference-typed) property, so EF detects in-place mutations correctly.
    /// </summary>
    public static ValueComparer<T> Comparer<T>() => new(
        (left, right) => Serialize(left) == Serialize(right),
        value => Serialize(value).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(Serialize(value), Options)!);

    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, Options);
}
