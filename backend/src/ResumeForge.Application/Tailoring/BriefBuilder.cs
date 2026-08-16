using System.Text;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="IBriefBuilder"/> producing a delimiter-separated plain-text
/// brief rather than JSON, to keep it as small as possible: one line per requirement
/// (id, mandatory flag, truncated text) and one line per candidate (id, available variant
/// count for bullets, truncated text), capped at <see cref="TailorOptions.CandidateLimit"/>
/// candidates per section.
/// </summary>
public sealed class BriefBuilder : IBriefBuilder
{
    private const int RequirementTextCap = 100;
    private const int CandidateTextCap = 140;

    /// <inheritdoc />
    public string Build(JobAnalysis analysis, CandidateSet candidates, ResumeDocument baseResume, TailorOptions options)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(baseResume);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();

        sb.Append("EFFORT|").Append(EffortToken(options.Effort)).Append('\n');

        sb.Append("REQUIREMENTS\n");
        foreach (var requirement in analysis.Requirements)
        {
            sb.Append(requirement.Id).Append('|')
              .Append(requirement.IsMandatory ? 'M' : 'P').Append('|')
              .Append(Truncate(requirement.Text, RequirementTextCap)).Append('\n');
        }

        AppendBulletCandidates(sb, "CANDIDATES-EXPERIENCE", candidates.Experience, baseResume, options.CandidateLimit);
        AppendBulletCandidates(sb, "CANDIDATES-PROJECTS", candidates.Projects, baseResume, options.CandidateLimit);
        AppendSkillCandidates(sb, candidates.Skills, options.CandidateLimit);

        // injectKeywords is only available at Thorough and above (CONTRACTS.md §6), so the
        // candidate keyword pool — the JD terms the taxonomy recognizes, which is exactly
        // what CommandValidator's rule 6 and HeuristicLanguageModel's KB-evidence check
        // reason about — is only worth the tokens to include at that effort or higher.
        if (options.Effort >= ModelEffort.Thorough)
        {
            sb.Append("KEYWORDS\n");
            foreach (var keyword in analysis.MatchedSkills)
            {
                sb.Append(keyword).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string EffortToken(ModelEffort effort) => effort switch
    {
        ModelEffort.Minimal => "minimal",
        ModelEffort.Standard => "standard",
        ModelEffort.Thorough => "thorough",
        ModelEffort.Maximum => "maximum",
        _ => "standard",
    };

    /// <inheritdoc />
    public int EstimateTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private static void AppendBulletCandidates(
        StringBuilder sb, string header, IReadOnlyList<ScoredCandidate> items, ResumeDocument baseResume, int limit)
    {
        sb.Append(header).Append('\n');

        foreach (var candidate in items.Take(Math.Max(0, limit)))
        {
            var variantCount = 0;
            if (EntityId.TryParse(candidate.EntityId, out var id) && baseResume.TryFindBullet(id, out var bullet))
            {
                variantCount = bullet.Variants.Count;
            }

            sb.Append(candidate.EntityId).Append("|v").Append(variantCount).Append('|')
              .Append(Truncate(candidate.Text, CandidateTextCap)).Append('\n');
        }
    }

    private static void AppendSkillCandidates(StringBuilder sb, IReadOnlyList<ScoredCandidate> items, int limit)
    {
        sb.Append("CANDIDATES-SKILLS\n");

        foreach (var candidate in items.Take(Math.Max(0, limit)))
        {
            sb.Append(candidate.EntityId).Append('|')
              .Append(Truncate(candidate.Text, CandidateTextCap)).Append('\n');
        }
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, Math.Max(0, max - 1)), "…");
    }
}
