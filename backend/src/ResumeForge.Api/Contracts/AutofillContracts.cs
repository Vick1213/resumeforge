using ResumeForge.Application.Tailoring;

namespace ResumeForge.Api.Contracts;

/// <summary>
/// Response for <c>GET /api/autofill/profile</c>, consumed by the browser extension.
/// CONTRACTS.md §10 declares this type under the <c>ResumeForge.Application.Autofill</c>
/// namespace ("shared verbatim with the extension"), but it does not exist anywhere in the
/// solution and Application is out of scope for this layer, so it is defined here with the
/// same wire shape instead — see the deviation note in the implementation report.
/// </summary>
public sealed record AutofillProfile
{
    /// <summary>Canonical field key → value. Every key in the closed canonical set is present, null when unknown.</summary>
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }

    /// <summary>Downloadable documents (resume, cover letter) available to attach.</summary>
    public required IReadOnlyList<AutofillDocument> Documents { get; init; }
}

/// <summary>A single downloadable document offered to the extension for attachment.</summary>
public sealed record AutofillDocument
{
    /// <summary>The document's kind: <c>"resume"</c> or <c>"coverLetter"</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Suggested file name for the download.</summary>
    public required string FileName { get; init; }

    /// <summary>URL the extension can use to obtain the document's bytes.</summary>
    public required string DownloadUrl { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/autofill/resolve</c> — the tier-3 model fallback for
/// fields the extension's declarative and heuristic resolution could not confidently map.
/// </summary>
public sealed record ResolveFieldsRequest
{
    /// <summary>The hostname the form was observed on, e.g. <c>"boards.greenhouse.io"</c>.</summary>
    public required string Host { get; init; }

    /// <summary>A stable hash of the form's field set.</summary>
    public required string FormSignature { get; init; }

    /// <summary>The fields the extension could not resolve on its own.</summary>
    public required IReadOnlyList<UnresolvedField> Fields { get; init; }

    /// <summary>
    /// How much decision-making budget tier 3 may spend the model on (CONTRACTS.md §10).
    /// Governs tier-3 behaviour only — select/radio option choice at
    /// <see cref="ModelEffort.Standard"/>, free-text answers for open questions at
    /// <see cref="ModelEffort.Thorough"/> and above, a longer per-answer budget at
    /// <see cref="ModelEffort.Maximum"/>. The tier-2 heuristic accept threshold is enforced
    /// entirely in the extension and is not affected by this field.
    /// </summary>
    public ModelEffort Effort { get; init; } = ModelEffort.Standard;
}

/// <summary>A single form field the extension's client-side resolution could not confidently map.</summary>
public sealed record UnresolvedField
{
    /// <summary>Extension-assigned element id, stable per render.</summary>
    public required string ElementId { get; init; }

    /// <summary>The field's associated label text, if any.</summary>
    public string? Label { get; init; }

    /// <summary>The field's <c>name</c> attribute, if any.</summary>
    public string? Name { get; init; }

    /// <summary>The field's placeholder text, if any.</summary>
    public string? Placeholder { get; init; }

    /// <summary>The field's <c>autocomplete</c> attribute, if any.</summary>
    public string? AutoComplete { get; init; }

    /// <summary>The field's input type: <c>text|email|tel|select|radio|checkbox|file|textarea</c>.</summary>
    public required string InputType { get; init; }

    /// <summary>Available options, for <c>select</c>/<c>radio</c> fields.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>Response for <c>POST /api/autofill/resolve</c>.</summary>
public sealed record ResolveFieldsResponse
{
    /// <summary>The model's proposed resolution for each requested field.</summary>
    public required IReadOnlyList<FieldResolution> Resolutions { get; init; }

    /// <summary>Tokens spent producing this response.</summary>
    public required ResumeForge.Application.Abstractions.TokenUsage Usage { get; init; }
}

/// <summary>A single field's proposed resolution.</summary>
public sealed record FieldResolution
{
    /// <summary>The element id this resolution is for.</summary>
    public required string ElementId { get; init; }

    /// <summary>The resolved canonical field key, or empty when genuinely unmappable.</summary>
    public required string CanonicalKey { get; init; }

    /// <summary>Confidence of the resolution, 0..1.</summary>
    public required double Confidence { get; init; }

    /// <summary>The chosen option value, for <c>select</c>/<c>radio</c> fields.</summary>
    public string? OptionValue { get; init; }
}
