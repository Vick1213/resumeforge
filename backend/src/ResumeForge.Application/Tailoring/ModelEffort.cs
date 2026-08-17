namespace ResumeForge.Application.Tailoring;

/// <summary>
/// How much decision-making budget a tailoring or autofill-resolution run is allowed to
/// spend the model on (CONTRACTS.md §6, §10). Effort is purely additive: <see cref="Standard"/>
/// is the default and reproduces exactly the behaviour that existed before this type did —
/// an omitted effort must never change output. Serializes as its lowercase name via the
/// API host's global <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>.
/// </summary>
public enum ModelEffort
{
    /// <summary>Selection and ordering only — no rewrites, no keyword injection. <c>MaxRewrites</c> 0.</summary>
    Minimal,

    /// <summary>
    /// The default, and what every pre-effort behaviour maps to. Enables <c>rewrite</c> and
    /// <c>setSummary</c>. <c>MaxRewrites</c> 6.
    /// </summary>
    Standard,

    /// <summary>Additionally enables <c>injectKeywords</c>. <c>MaxRewrites</c> 12.</summary>
    Thorough,

    /// <summary>Regenerates the summary every run. <c>MaxRewrites</c> 20.</summary>
    Maximum,

    /// <summary>
    /// Every op, on every element, with the rewrite budget effectively lifted: additionally
    /// enables <c>setTagline</c>, so a project's one-line description is rewritable too, and
    /// nothing in the document is off-limits to the model's judgement. <c>MaxRewrites</c> 200.
    /// </summary>
    /// <remarks>
    /// The one thing this tier does *not* relax is the fabrication guard. "Change anything it
    /// doesn't like" means rephrase, reorder, and re-emphasize anything — never invent an
    /// employer, a date, or a metric the knowledge base cannot support. That rule holds
    /// identically here and at <see cref="Minimal"/> (CONTRACTS.md §6).
    /// </remarks>
    Full,
}
