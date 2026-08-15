using ResumeForge.Application.Analysis;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="ICoverageAnalyzer"/>. A requirement is covered when at least
/// one bullet or skill whose parent is <em>included</em> carries a matching skill tag.
/// <see cref="CoverageReport.Score"/> considers only mandatory requirements, and is 1.0
/// when the job posting has none.
/// </summary>
public sealed class CoverageAnalyzer : ICoverageAnalyzer
{
    /// <inheritdoc />
    public CoverageReport Analyze(ResumeDocument document, JobAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(analysis);

        var includedBullets = CollectIncludedBullets(document);
        var includedSkills = CollectIncludedSkills(document);

        var requirementCoverages = new List<RequirementCoverage>(analysis.Requirements.Count);

        foreach (var requirement in analysis.Requirements)
        {
            var evidence = new List<string>();

            if (requirement.Skills.Count > 0)
            {
                foreach (var (bulletId, tags) in includedBullets)
                {
                    if (requirement.Skills.Any(s => tags.Contains(s, StringComparer.Ordinal)))
                    {
                        evidence.Add(bulletId);
                    }
                }

                foreach (var (skillId, normalized) in includedSkills)
                {
                    if (requirement.Skills.Contains(normalized, StringComparer.Ordinal))
                    {
                        evidence.Add(skillId);
                    }
                }
            }

            evidence = [.. evidence.OrderBy(e => e, StringComparer.Ordinal)];

            requirementCoverages.Add(new RequirementCoverage
            {
                RequirementId = requirement.Id,
                RequirementText = requirement.Text,
                Covered = evidence.Count > 0,
                EvidenceIds = evidence,
            });
        }

        var mandatoryIds = new HashSet<string>(
            analysis.Requirements.Where(r => r.IsMandatory).Select(r => r.Id),
            StringComparer.Ordinal);

        var mandatoryCovered = requirementCoverages.Count(rc => mandatoryIds.Contains(rc.RequirementId) && rc.Covered);
        var score = mandatoryIds.Count == 0 ? 1.0 : (double)mandatoryCovered / mandatoryIds.Count;

        return new CoverageReport { Score = score, Requirements = requirementCoverages };
    }

    private static List<(string Id, IReadOnlyList<string> Tags)> CollectIncludedBullets(ResumeDocument document)
    {
        var result = new List<(string, IReadOnlyList<string>)>();

        foreach (var entry in document.Experience.Where(e => e.Included))
        {
            foreach (var bullet in entry.Bullets)
            {
                result.Add((bullet.Id, bullet.Tags));
            }
        }

        foreach (var entry in document.Projects.Where(p => p.Included))
        {
            foreach (var bullet in entry.Bullets)
            {
                result.Add((bullet.Id, bullet.Tags));
            }
        }

        return result;
    }

    private static List<(string Id, string Normalized)> CollectIncludedSkills(ResumeDocument document)
    {
        var result = new List<(string, string)>();

        foreach (var group in document.Skills.Where(g => g.Included))
        {
            foreach (var skill in group.Items)
            {
                result.Add((skill.Id, skill.Normalized));
            }
        }

        return result;
    }
}
