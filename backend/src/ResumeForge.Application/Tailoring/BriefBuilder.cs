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
    /// <summary>
    /// A requirement line's character budget. Generous enough to carry a whole requirement:
    /// the old 100-character cap cut most of them mid-clause, which cost the model the very
    /// detail ("...with 3+ years of Kubernetes" / "...experience mentoring engineers") that
    /// distinguishes one requirement from another.
    /// </summary>
    private const int RequirementTextCap = 300;

    private const int CandidateTextCap = 140;

    /// <inheritdoc />
    public string Build(
        JobPosting posting, JobAnalysis analysis, CandidateSet candidates, ResumeDocument baseResume, TailorOptions options)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(baseResume);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();

        sb.Append("EFFORT|").Append(EffortToken(options.Effort)).Append('\n');

        sb.Append("JOB|")
          .Append(Sanitize(posting.Title)).Append('|')
          .Append(Sanitize(posting.Company)).Append('|')
          .Append(Sanitize(posting.Location)).Append('|')
          .Append(analysis.Seniority.ToString().ToLowerInvariant()).Append('\n');

        sb.Append("REQUIREMENTS\n");
        foreach (var requirement in analysis.Requirements)
        {
            sb.Append(requirement.Id).Append('|')
              .Append(requirement.IsMandatory ? 'M' : 'P').Append('|')
              .Append(Truncate(requirement.Text, RequirementTextCap)).Append('\n');
        }

        AppendPosting(sb, posting.RawText, options.PostingExcerptChars);

        AppendBulletCandidates(sb, "CANDIDATES-EXPERIENCE", candidates.Experience, baseResume, options.CandidateLimit);
        AppendBulletCandidates(sb, "CANDIDATES-PROJECTS", candidates.Projects, baseResume, options.CandidateLimit);
        AppendSkillCandidates(sb, candidates.Skills, options.CandidateLimit);

        // setTagline is Full-only (CONTRACTS.md §6), and the model cannot rewrite a line it
        // has never seen: every other section keys off *bullet* ids, so without this the
        // project's own id and current description are absent from the brief entirely.
        if (options.Effort >= ModelEffort.Full)
        {
            sb.Append("TAGLINES\n");
            foreach (var project in baseResume.Projects.Where(p => p.Included))
            {
                sb.Append(project.Id).Append('|')
                  .Append(Truncate(CollapseSpaces(project.Tagline ?? string.Empty), CandidateTextCap)).Append('\n');
            }
        }

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

    /// <summary>
    /// Appends the posting's own text, collapsed and bounded. Blank lines and runs of spaces
    /// are squeezed out first because scraped postings are mostly whitespace, and paying
    /// tokens for it would buy nothing. Truncation is at a line boundary rather than
    /// mid-sentence, so the model never has to reason about a half-written requirement.
    /// </summary>
    private static void AppendPosting(StringBuilder sb, string rawText, int excerptChars)
    {
        if (excerptChars <= 0 || string.IsNullOrWhiteSpace(rawText))
        {
            return;
        }

        sb.Append("POSTING\n");

        var used = 0;
        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = CollapseSpaces(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            if (used + line.Length > excerptChars)
            {
                sb.Append("…(posting truncated)\n");
                break;
            }

            sb.Append(line).Append('\n');
            used += line.Length + 1;
        }
    }

    private static string CollapseSpaces(string line)
    {
        var trimmed = line.AsSpan().Trim();
        var sb = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;

        foreach (var c in trimmed)
        {
            var isSpace = char.IsWhiteSpace(c);
            if (isSpace && lastWasSpace)
            {
                continue;
            }

            sb.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips the delimiter and newlines from a field interpolated into a pipe-delimited
    /// line, so a posting whose title contains a '|' cannot shift every field after it.
    /// </summary>
    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : CollapseSpaces(value.Replace('|', '/'));

    private static string EffortToken(ModelEffort effort) => effort switch
    {
        ModelEffort.Minimal => "minimal",
        ModelEffort.Standard => "standard",
        ModelEffort.Thorough => "thorough",
        ModelEffort.Maximum => "maximum",
        ModelEffort.Full => "full",
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
