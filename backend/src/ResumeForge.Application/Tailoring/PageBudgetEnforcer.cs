using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Scoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="IPageBudgetEnforcer"/> (CONTRACTS.md §6 "Page budget"). Cut
/// order is ascending relevance score — the single lowest-scoring still-included entry
/// goes first each pass — with section kind breaking exact ties: certifications, then
/// projects. Certifications carry no relevance score of their own (they are not part of
/// <see cref="CandidateSet"/> per CONTRACTS.md §5), so every certification is scored
/// <c>0.0</c>, which in practice puts them at or ahead of the front of the cut order
/// without needing a separate rule.
/// Experience and education entries are never cut: a job seeker's employment history is
/// the substance of a resume — a budget squeeze may surrender side projects and
/// certifications, but a resume that silently dropped a job reads as a gap in the
/// candidate's history, which is worse than a resume that runs long. Only the user, via
/// <see cref="TailorOptions.ExcludedEntryIds"/>, may remove a job.
/// An entry's score is the mean relevance of its own scored bullets (0.0 for an entry with
/// none), read from the <see cref="CandidateSet"/> computed earlier in the tailoring graph.
/// <see cref="TailorOptions.PinnedEntryIds"/> (CONTRACTS.md §6 "Forced inclusion") removes
/// an entry, of any cuttable kind, from the cut candidates too, and if pins alone can't fit
/// the budget the loop stops rather than cutting one —
/// <see cref="PageBudgetResult.FitsBudget"/> reports false exactly as it already does when
/// the floor can't fit.
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

        // Pinned entries (CONTRACTS.md §6 "Forced inclusion") are never cut candidates.
        // ExecuteCommands has already forced each pinned entry's Included flag to true by
        // the time this node runs, so no extra Included check is needed here beyond the
        // usual cuttable filter.
        var neverCut = new HashSet<string>(options.PinnedEntryIds, StringComparer.Ordinal);

        var workingDocument = document;
        var workingDiff = new List<ResumeDiffEntry>(diff);
        var passes = 0;

        // Each pass removes exactly one entry from a finite, shrinking candidate set, so
        // the loop is inherently bounded by the number of cuttable entries in the document.
        // MaxPageBudgetPasses is a second, explicit ceiling on top of that natural bound, so
        // an adversarially large knowledge base can never force an unbounded number of
        // renders regardless of how the cutting logic evolves.
        var maxPasses = Math.Min(CountCuttable(workingDocument, neverCut), Math.Max(0, options.MaxPageBudgetPasses));

        while (pageCount > maxPages.Value && passes < maxPasses)
        {
            var victim = FindLowestScoringCuttableEntry(workingDocument, scores, neverCut);
            if (victim is null)
            {
                break; // nothing cuttable remains — experience and education are never cut
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
        _ => 2,
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

    private static IEnumerable<CuttableEntry> EnumerateCuttable(
        ResumeDocument document, IReadOnlyDictionary<string, double> scores, IReadOnlySet<string> neverCut)
    {
        foreach (var cert in document.Certifications.Where(c => c.Included && !neverCut.Contains(c.Id)))
        {
            yield return new CuttableEntry(cert.Id, EntityKind.Certification, 0.0, cert.Name);
        }

        foreach (var project in document.Projects.Where(p => p.Included && !neverCut.Contains(p.Id)))
        {
            yield return new CuttableEntry(project.Id, EntityKind.Project, scores.GetValueOrDefault(project.Id, 0.0), project.Name);
        }
    }

    private static int CountCuttable(ResumeDocument document, IReadOnlySet<string> neverCut) =>
        document.Certifications.Count(c => c.Included && !neverCut.Contains(c.Id))
        + document.Projects.Count(p => p.Included && !neverCut.Contains(p.Id));

    private static CuttableEntry? FindLowestScoringCuttableEntry(
        ResumeDocument document, IReadOnlyDictionary<string, double> scores, IReadOnlySet<string> neverCut)
    {
        CuttableEntry? lowest = null;

        foreach (var entry in EnumerateCuttable(document, scores, neverCut))
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
