namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// A parse warning or error raised while reading a single knowledge-base file. A file
/// that produces an <see cref="DiagnosticSeverity.Error"/> diagnostic is skipped, but the
/// rest of the knowledge base still loads.
/// </summary>
public sealed record KnowledgeBaseDiagnostic
{
    /// <summary>Path of the file that produced this diagnostic.</summary>
    public required string FilePath { get; init; }

    /// <summary>1-based line number within the file, where applicable.</summary>
    public required int Line { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>Severity of the issue.</summary>
    public required DiagnosticSeverity Severity { get; init; }
}
