namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to the bundled skill taxonomy (alias → canonical name, canonical → category),
/// backed in Infrastructure by <c>ResumeForge.Infrastructure/Data/skills.json</c>. Used
/// throughout Application to canonicalize free-text skill mentions so job descriptions,
/// knowledge-base <c>tech:</c> lists, and rendered resumes all agree on one spelling.
/// </summary>
public interface ISkillTaxonomy
{
    /// <summary>
    /// Attempts to resolve <paramref name="raw"/> (typically already passed through
    /// <see cref="Domain.Text.SkillNormalizer.Normalize"/>) to its canonical taxonomy
    /// entry. Returns false when the taxonomy has no known mapping for it.
    /// </summary>
    bool TryCanonicalize(string raw, out string canonical);

    /// <summary>Every canonical skill name known to the taxonomy.</summary>
    IReadOnlySet<string> AllCanonical { get; }

    /// <summary>The known alias spellings that canonicalize to <paramref name="canonical"/>.</summary>
    IReadOnlyList<string> AliasesFor(string canonical);

    /// <summary>
    /// The human-facing spelling for <paramref name="canonical"/> (e.g. <c>"C#"</c> for
    /// <c>"csharp"</c>, <c>".NET"</c> for <c>"dotnet"</c>) — what user-visible prose (a
    /// rendered bullet, a rewritten sentence) should print instead of the normalized match
    /// key. Falls back to <paramref name="canonical"/> itself when the taxonomy has no
    /// display name recorded for it, so a caller never has to special-case an unknown name.
    /// </summary>
    string DisplayNameOf(string canonical);

    /// <summary>
    /// The taxonomy category a canonical skill belongs to — one of
    /// <c>languages</c>, <c>frameworks</c>, <c>datastores</c>, <c>cloud</c>,
    /// <c>practices</c>, <c>tools</c>, or <c>soft</c>. Used to bucket derived skill
    /// groups on the resume. Callers should only invoke this for names known to
    /// <see cref="AllCanonical"/>; an unrecognized canonical name yields <c>"other"</c>.
    /// </summary>
    string CategoryOf(string canonical);
}
