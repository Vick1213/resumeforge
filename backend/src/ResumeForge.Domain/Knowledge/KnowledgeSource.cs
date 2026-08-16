using System.Text.Json.Serialization;

namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// Where a knowledge item's content originated. Served directly as
/// <c>KnowledgeItemDto.Source</c>/<c>KnowledgeItemDetailDto.Source</c> via the API host's
/// global <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>.
/// </summary>
public enum KnowledgeSource
{
    /// <summary>Hand-written by the user.</summary>
    Manual,

    /// <summary>
    /// Generated from a GitHub import and safe to regenerate. Pinned to serialize as
    /// <c>"github"</c> rather than the naming policy's own default conversion of this member
    /// name (<c>"gitHub"</c>): per the frontend integration ruling,
    /// <c>frontend/src/api/types.ts</c> is authoritative for wire shapes CONTRACTS.md leaves
    /// undefined, and its <c>KnowledgeSource</c> union names this value all-lowercase.
    /// </summary>
    [JsonStringEnumMemberName("github")]
    GitHub,
}
