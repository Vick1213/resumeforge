namespace ResumeForge.Application.Autofill;

/// <summary>
/// A previously learned mapping from a specific form's elements to canonical autofill
/// field keys, keyed by <c>(Host, FormSignature)</c> so the same form costs zero model
/// tokens on a repeat visit. Namespace and shape are shared verbatim with the browser
/// extension (mirrored in <c>extension/src/contracts.ts</c>). See CONTRACTS.md §10.
/// </summary>
public sealed record LearnedFieldMap
{
    /// <summary>The hostname the form was observed on, e.g. <c>"boards.greenhouse.io"</c>.</summary>
    public required string Host { get; init; }

    /// <summary>A stable hash of the form's field set.</summary>
    public required string FormSignature { get; init; }

    /// <summary>Extension-assigned element id → canonical field key.</summary>
    public required IReadOnlyDictionary<string, string> ElementToKey { get; init; }

    /// <summary>When this mapping was learned.</summary>
    public required DateTimeOffset LearnedAt { get; init; }

    /// <summary>How many times this mapping has been reused since being learned.</summary>
    public int HitCount { get; init; }
}
