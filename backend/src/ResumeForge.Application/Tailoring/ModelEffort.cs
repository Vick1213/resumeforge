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
}
