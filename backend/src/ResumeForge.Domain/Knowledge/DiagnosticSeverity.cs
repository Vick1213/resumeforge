namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// Severity of a <see cref="KnowledgeBaseDiagnostic"/> raised while reading the knowledge base.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>A non-fatal issue; the file is still loaded where possible.</summary>
    Warning,

    /// <summary>A fatal issue; the offending file is skipped.</summary>
    Error,
}
