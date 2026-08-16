using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="IPageBudgetEnforcer"/> (CONTRACTS.md §6 "Page budget"). Cut
/// order is ascending relevance score — the single lowest-scoring still-included entry
/// goes first each pass — with section kind breaking exact ties: certifications, then
/// projects, then experience, since a job seeker's employment history is the substance of
/// a resume and side projects/certifications are the padding. Certifications carry no
/// relevance score of their own (they are not part of <see cref="CandidateSet"/> per
/// CONTRACTS.md §5), so every certification is scored <c>0.0</c>, which in practice puts
/// them at or ahead of the front of the cut order without needing a separate rule.
/// Education entries are never cut: CONTRACTS.md §6 names only certifications, projects,
/// and experience as surrenderable, and education has no relevance score to rank it by.
/// An entry's score is the mean relevance of its own scored bullets (0.0 for an entry with
/// none), read from the <see cref="CandidateSet"/> computed earlier in the tailoring graph.
/// </summary>
public sealed class PageBudgetEnforcer(IResumeRenderer renderer) : IPageBudgetEnforcer
{
    /// <inheritdoc />
    public async Task<PageBudgetResult> EnforceAsync(
        ResumeDocument document,
        IReadOnlyList<ResumeDiffEntry> diff,
        CandidateSet candidates,
        TailorOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        var pageCount = await RenderAndCountAsync(document, ct).ConfigureAwait(false);
        var maxPages = options.MaxPages;

        if (maxPages is null || pageCount <= maxPages.Value)
        {
            return new PageBudgetResult { Document = document, Diff = diff, PageCount = pageCount, FitsBudget = true };
        }

        var scores = BuildEntryScores(candidates);
        var floorExperienceId = FindFloorExperienceId(document, scores);

        var workingDocument = document;
        var workingDiff = new List<ResumeDiffEntry>(diff);
        var passes = 0;

        // Each pass removes exactly one entry from a finite, shrinking candidate set, so
        // the loop is inherently bounded by the number of cuttable entries in the document.
        // MaxPageBudgetPasses is a second, explicit ceiling on top of that natural bound, so
        // an adversarially large knowledge base can never force an unbounded number of
        // renders regardless of how the cutting logic evolves.
        var maxPasses = Math.Min(CountCuttable(workingDocument, floorExperienceId), Math.Max(0, options.MaxPageBudgetPasses));

        while (pageCount > maxPages.Value && passes < maxPasses)
        {
            var victim = FindLowestScoringCuttableEntry(workingDocument, scores, floorExperienceId);
            if (victim is null)
            {
                break; // only the floor remains
            }

            workingDocument = Exclude(workingDocument, victim.Value, maxPages.Value, out var diffEntry);
            workingDiff.Add(diffEntry);
            pageCount = await RenderAndCountAsync(workingDocument, ct).ConfigureAwait(false);
            passes++;
        }

        return new PageBudgetResult
        {
            Document = workingDocument,
            Diff = workingDiff,
            PageCount = pageCount,
            FitsBudget = pageCount <= maxPages.Value,
        };
    }

    private async Task<int> RenderAndCountAsync(ResumeDocument document, CancellationToken ct)
    {
        var rendered = await renderer.RenderAsync(document, RenderFormat.Pdf, ct).ConfigureAwait(false);

        // PageCount is only null for non-paginated formats (CONTRACTS.md §6 implementation
        // note); Pdf always reports one, but a 1-page fallback keeps this defensive rather
        // than throwing if a future renderer swap ever left it unset.
        return rendered.PageCount ?? 1;
    }

    private readonly record struct CuttableEntry(string Id, EntityKind Kind, double Score, string Label);

    private static int KindPriority(EntityKind kind) => kind switch
    {
        EntityKind.Certification => 0,
        EntityKind.Project => 1,
        EntityKind.Experience => 2,
        _ => 3,
    };

    /// <summary>
    /// One score per experience/project entry: the mean relevance of its own bullets, as
    /// scored earlier in the graph. Bullet ids are stable and never renumbered (CONTRACTS.md
    /// §6), so mapping a bullet id back to its owning entry id via <see cref="EntityId.Parent"/>
    /// is reliable even though <see cref="CandidateSet"/> only carries bullet-level scores.
    /// </summary>
    private static Dictionary<string, double> BuildEntryScores(CandidateSet candidates)
    {
        var sums = new Dictionary<string, (double Sum, int Count)>(StringComparer.Ordinal);

        void Accumulate(IEnumerable<ScoredCandidate> scored)
        {
            foreach (var candidate in scored)
            {
                if (!EntityId.TryParse(candidate.EntityId, out var id))
                {
                    continue;
                }

                var parent = id.Parent.ToString();
                var (sum, count) = sums.TryGetValue(parent, out var existing) ? existing : (0.0, 0);
                sums[parent] = (sum + candidate.Score, count + 1);
            }
        }

        Accumulate(candidates.Experience);
        Accumulate(candidates.Projects);

        return sums.ToDictionary(kv => kv.Key, kv => kv.Value.Sum / kv.Value.Count, StringComparer.Ordinal);
    }

    /// <summary>
    /// The floor: the highest-scoring still-included experience entry, which is never a cut
    /// candidate (CONTRACTS.md §6). Null when there are no included experience entries.
    /// </summary>
    private static string? FindFloorExperienceId(ResumeDocument document, IReadOnlyDictionary<string, double> scores) =>
        document.Experience
            .Where(e => e.Included)
            .Select(e => (e.Id, Score: scores.GetValueOrDefault(e.Id, 0.0)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => (string?)x.Id)
            .FirstOrDefault();

    private static IEnumerable<CuttableEntry> EnumerateCuttable(
        ResumeDocument document, IReadOnlyDictionary<string, double> scores, string? floorExperienceId)
    {
        foreach (var cert in document.Certifications.Where(c => c.Included))
        {
            yield return new CuttableEntry(cert.Id, EntityKind.Certification, 0.0, cert.Name);
        }

        foreach (var project in document.Projects.Where(p => p.Included))
        {
            yield return new CuttableEntry(project.Id, EntityKind.Project, scores.GetValueOrDefault(project.Id, 0.0), project.Name);
        }

        foreach (var experience in document.Experience.Where(e => e.Included && e.Id != floorExperienceId))
        {
            yield return new CuttableEntry(
                experience.Id,
                EntityKind.Experience,
                scores.GetValueOrDefault(experience.Id, 0.0),
                $"{experience.Role} at {experience.Organization}");
        }
    }

    private static int CountCuttable(ResumeDocument document, string? floorExperienceId) =>
        document.Certifications.Count(c => c.Included)
        + document.Projects.Count(p => p.Included)
        + document.Experience.Count(e => e.Included && e.Id != floorExperienceId);

    private static CuttableEntry? FindLowestScoringCuttableEntry(
        ResumeDocument document, IReadOnlyDictionary<string, double> scores, string? floorExperienceId)
    {
        CuttableEntry? lowest = null;

        foreach (var entry in EnumerateCuttable(document, scores, floorExperienceId))
        {
            if (lowest is not { } current ||
                entry.Score < current.Score ||
                (entry.Score == current.Score && KindPriority(entry.Kind) < KindPriority(current.Kind)) ||
                (entry.Score == current.Score && KindPriority(entry.Kind) == KindPriority(current.Kind) &&
                 string.CompareOrdinal(entry.Id, current.Id) < 0))
            {
                lowest = entry;
            }
        }

        return lowest;
    }

    private static ResumeDocument Exclude(ResumeDocument document, CuttableEntry victim, int maxPages, out ResumeDiffEntry diffEntry)
    {
        var trimmed = victim.Kind switch
        {
            EntityKind.Certification => document with
            {
                Certifications = [.. document.Certifications.Select(c => c.Id == victim.Id ? c with { Included = false } : c)],
            },
            EntityKind.Project => document with
            {
                Projects = [.. document.Projects.Select(p => p.Id == victim.Id ? p with { Included = false } : p)],
            },
            EntityKind.Experience => document with
            {
                Experience = [.. document.Experience.Select(e => e.Id == victim.Id ? e with { Included = false } : e)],
            },
            _ => document,
        };

        diffEntry = new ResumeDiffEntry
        {
            EntityId = victim.Id,
            Kind = DiffKind.Excluded,
            Before = victim.Label,
            After = null,
            Rationale = $"Excluded to fit the {maxPages}-page budget.",
        };

        return trimmed;
    }
}
