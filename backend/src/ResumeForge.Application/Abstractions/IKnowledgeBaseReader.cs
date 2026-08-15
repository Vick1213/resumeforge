using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port that reads the markdown knowledge base under <c>profile/</c> into memory.
/// Implemented in Infrastructure against the filesystem.
/// </summary>
public interface IKnowledgeBaseReader
{
    /// <summary>Reads every knowledge item and the basics profile in one pass.</summary>
    Task<KnowledgeBaseSnapshot> ReadAsync(CancellationToken ct);
}

/// <summary>
/// A full read of the knowledge base at a point in time: every parsed item, the basics
/// profile, the default summary, and any diagnostics raised while parsing.
/// </summary>
public sealed record KnowledgeBaseSnapshot
{
    /// <summary>Every successfully parsed knowledge item, across all categories.</summary>
    public required IReadOnlyList<KnowledgeItem> Items { get; init; }

    /// <summary>Contact and identity information parsed from <c>profile/basics.md</c>.</summary>
    public required ResumeBasics Basics { get; init; }

    /// <summary>The default summary body from <c>profile/basics.md</c>, if present.</summary>
    public string? DefaultSummary { get; init; }

    /// <summary>Parse warnings and errors raised while reading the knowledge base.</summary>
    public required IReadOnlyList<KnowledgeBaseDiagnostic> Diagnostics { get; init; }
}
