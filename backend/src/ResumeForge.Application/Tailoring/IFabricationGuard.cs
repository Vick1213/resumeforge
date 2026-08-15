namespace ResumeForge.Application.Tailoring;

/// <summary>
/// The anti-fabrication check behind command-validation rule 3, and the single most
/// important correctness rule in the product: a rewrite may re-angle a bullet's phrasing,
/// but it may never invent a metric or drop the evidence for one that already existed.
/// </summary>
public interface IFabricationGuard
{
    /// <summary>
    /// Checks <paramref name="rewrittenText"/> against <paramref name="originalText"/>.
    /// Fails when the rewrite introduces a number absent from the original (reformatting
    /// — <c>"1,200"</c> vs <c>"1200"</c>, case — <c>"p99"</c> vs <c>"P99"</c> — does not
    /// count as a new number), or when the original contained a number or proper noun and
    /// the rewrite retains none of them.
    /// </summary>
    bool IsSafe(string originalText, string rewrittenText, out string? reason);
}
