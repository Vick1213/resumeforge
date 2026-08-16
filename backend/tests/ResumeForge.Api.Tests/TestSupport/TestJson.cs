using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeForge.Api.Tests.TestSupport;

/// <summary>
/// The same JSON wire format the running app uses (CONTRACTS.md §1: camelCase properties,
/// camelCase enum strings), so requests built in tests serialize the same way a real client
/// would and responses deserialize back into the C# request/response records.
/// </summary>
internal static class TestJson
{
    /// <summary>Shared options mirroring <c>Program.cs</c>'s <c>ConfigureHttpJsonOptions</c> call.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
