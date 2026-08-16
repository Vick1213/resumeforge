namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// A parse warning or error raised while reading a single knowledge-base file. A file
/// that produces an <see cref="DiagnosticSeverity.Error"/> diagnostic is skipped, but the
/// rest of the knowledge base still loads. Served directly (unwrapped by any Api-layer DTO)
/// as <c>ProfileDto.Diagnostics</c> and <c>KnowledgeItemDetailDto.Diagnostics</c>; per the
/// frontend integration ruling, <c>frontend/src/api/types.ts</c> is authoritative for shapes
/// CONTRACTS.md leaves undefined, so <see cref="File"/> is named to match its
/// <c>KnowledgeBaseDiagnostic.file</c> exactly, not the more verbose <c>filePath</c> this
/// property used to serialize as.
/// </summary>
public sealed record KnowledgeBaseDiagnostic
{
    /// <summary>Path of the file that produced this diagnostic.</summary>
    public required string File { get; init; }

    /// <summary>1-based line number within the file, where applicable.</summary>
    public required int Line { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>Severity of the issue.</summary>
    public required DiagnosticSeverity Severity { get; init; }
}
