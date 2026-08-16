using System.Text.RegularExpressions;
using ResumeForge.Application.Analysis;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Scoring;

/// <summary>
/// Deterministic <see cref="IRelevanceScorer"/> combining three normalized 0..1 terms:
/// BM25 (k1=1.2, b=0.75) textual relevance of the bullet against the job posting's
/// requirement text, canonical skill-tag overlap (Jaccard) against the job's skill set,
/// and recency of the owning entry (via the injected <see cref="TimeProvider"/>). Skills
/// have no prose or date, so they are scored purely by their rank among the job's
/// extracted keywords.
///
/// Experience bullets additionally carry a role-level fit factor (see
/// <see cref="LevelFitFactor"/>): BM25 rewards keyword density irrespective of how
/// substantial the surrounding entry is, so a terse, keyword-dense internship bullet can
/// otherwise outscore a longer bullet from a senior role that says the same thing with
/// more supporting words. The factor multiplies the composite score down, in proportion
/// to how far the entry's own title-derived <see cref="SeniorityLevel"/> falls short of
/// the job's, so an entry can never rank ahead of an equally-relevant entry that actually
/// meets the level the posting asks for.
/// </summary>
public sealed partial class Bm25RelevanceScorer(TimeProvider timeProvider, ScoringOptions options) : IRelevanceScorer
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const double RecencyHorizonMonths = 120.0;

    /// <summary>
    /// Per level of shortfall below the job's required <see cref="SeniorityLevel"/>, an
    /// entry's composite score is multiplied by this factor. Chosen so that a single
    /// level gap (e.g. Mid vs. a Senior posting) is a moderate penalty, while the multi
    /// level gap between an internship and a senior-or-above posting is severe enough
    /// that no plausible keyword-density advantage can close it.
    /// </summary>
    private const double LevelGapPenalty = 0.5;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "with", "for", "our", "you", "your", "we", "this", "that", "will", "have", "are", "is",
        "in", "on", "of", "to", "as", "at", "an", "be", "by", "or", "it", "its", "from", "not", "can", "all",
        "new", "team", "role", "work", "working", "about", "who", "what", "when", "where", "why", "how",
    };

    /// <inheritdoc />
    public CandidateSet Score(ResumeDocument baseResume, JobAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(baseResume);
        ArgumentNullException.ThrowIfNull(analysis);

        var queryTerms = BuildQueryTerms(analysis);
        var jdSkills = BuildJdSkillSet(analysis);
        var now = timeProvider.GetUtcNow();
        var (bm25W, skillW, recencyW) = options.Normalize();

        var experience = ScoreEntries(
            baseResume.Experience.Where(e => e.Included),
            e => e.Bullets,
            e => (e.StartDate, e.EndDate),
            e => SeniorityClassifier.FromTitle(e.Role),
            queryTerms, jdSkills, analysis, now, bm25W, skillW, recencyW);

        // Projects have no job title to classify, so they never carry a level gap penalty
        // — a side project ranking on its technical relevance alone is correct; there is
        // no "role level" to under-qualify.
        var projects = ScoreEntries(
            baseResume.Projects.Where(p => p.Included),
            p => p.Bullets,
            p => (p.StartDate, p.EndDate),
            _ => SeniorityLevel.Unknown,
            queryTerms, jdSkills, analysis, now, bm25W, skillW, recencyW);

        // A skill scores 0.0 exactly when it is absent from the job's keyword set (see
        // ScoreSkill below) — i.e. it is not evidenced by this job description at all.
        // Such skills are not tailoring candidates: offering them to the model as
        // "candidates" is indistinguishable, once truncated into the brief, from a
        // genuine match, and both the heuristic model and a real one would then have no
        // way to tell "the JD asked for this" apart from "this happens to be on the
        // resume" — which is exactly what turns emphasizeSkills into "emphasize
        // everything." Filtering here, once, keeps every downstream consumer of
        // CandidateSet.Skills correct without relying on each of them to re-derive it.
        var skills = baseResume.Skills
            .Where(g => g.Included)
            .SelectMany(g => g.Items)
            .Select(skill => ScoreSkill(skill, analysis))
            .Where(candidate => candidate.Score > 0.0)
            .ToList();

        return new CandidateSet
        {
            Experience = SortDeterministic(experience),
            Projects = SortDeterministic(projects),
            Skills = SortDeterministic(skills),
        };
    }

    private static List<ScoredCandidate> ScoreEntries<TEntry>(
        IEnumerable<TEntry> entries,
        Func<TEntry, IReadOnlyList<Bullet>> selectBullets,
        Func<TEntry, (DateOnly? Start, DateOnly? End)> selectDates,
        Func<TEntry, SeniorityLevel> selectLevel,
        IReadOnlyList<string> queryTerms,
        IReadOnlySet<string> jdSkills,
        JobAnalysis analysis,
        DateTimeOffset now,
        double bm25W,
        double skillW,
        double recencyW)
    {
        var entryList = entries.ToList();
        var bulletsWithDates = entryList
            .SelectMany(e => selectBullets(e).Select(b => (Bullet: b, Dates: selectDates(e), Level: selectLevel(e))))
            .ToList();

        var corpus = BuildCorpus(bulletsWithDates.Select(x => x.Bullet.Text));
        var rawBm25 = new double[bulletsWithDates.Count];
        for (var i = 0; i < bulletsWithDates.Count; i++)
        {
            rawBm25[i] = Bm25Score(corpus, i, queryTerms);
        }

        var maxBm25 = rawBm25.Length == 0 ? 0.0 : rawBm25.Max();

        var results = new List<ScoredCandidate>(bulletsWithDates.Count);
        for (var i = 0; i < bulletsWithDates.Count; i++)
        {
            var (bullet, dates, level) = bulletsWithDates[i];
            var bm25Norm = maxBm25 > 0 ? rawBm25[i] / maxBm25 : 0.0;
            var skillOverlap = Jaccard(bullet.Tags, jdSkills);
            var recency = ComputeRecency(dates.Start, dates.End, now);

            var composite = (bm25W * bm25Norm) + (skillW * skillOverlap) + (recencyW * recency);
            var score = Math.Clamp(composite * LevelFitFactor(level, analysis.Seniority), 0.0, 1.0);

            var matched = analysis.Requirements
                .Where(r => r.Skills.Count > 0 && r.Skills.Any(s => bullet.Tags.Contains(s, StringComparer.Ordinal)))
                .Select(r => r.Id)
                .ToList();

            results.Add(new ScoredCandidate
            {
                EntityId = bullet.Id,
                Text = bullet.Text,
                Score = score,
                MatchedRequirements = matched,
            });
        }

        return results;
    }

    private static ScoredCandidate ScoreSkill(Skill skill, JobAnalysis analysis)
    {
        var keywords = analysis.Keywords;
        var index = -1;
        for (var i = 0; i < keywords.Count; i++)
        {
            if (string.Equals(keywords[i], skill.Normalized, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        double score;
        List<string> matched;

        if (index >= 0)
        {
            // Top-ranked keyword scores 1.0; rank decays toward a 0.5 floor across the
            // ranked list, since presence in the job's keyword set is itself meaningful.
            score = keywords.Count <= 1 ? 1.0 : 1.0 - ((double)index / (keywords.Count - 1) * 0.5);
            matched = analysis.Requirements
                .Where(r => r.Skills.Contains(skill.Normalized, StringComparer.Ordinal))
                .Select(r => r.Id)
                .ToList();
        }
        else
        {
            score = 0.0;
            matched = [];
        }

        return new ScoredCandidate
        {
            EntityId = skill.Id,
            Text = skill.Name,
            Score = score,
            MatchedRequirements = matched,
        };
    }

    /// <summary>
    /// Multiplicative penalty for an entry whose title-derived <paramref name="entryLevel"/>
    /// falls short of the job's required <paramref name="jobLevel"/>. Returns 1.0 (no
    /// penalty) whenever either level could not be determined — a title with no
    /// recognizable seniority cue is not evidence of under-qualification, only of an
    /// ambiguous title — or whenever the entry already meets or exceeds the job's level.
    /// Otherwise the penalty compounds per level of gap (<see cref="LevelGapPenalty"/> per
    /// level), so the more an entry under-shoots what the job asks for, the less any
    /// keyword-density advantage in its bullet text can matter.
    /// </summary>
    private static double LevelFitFactor(SeniorityLevel entryLevel, SeniorityLevel jobLevel)
    {
        if (entryLevel == SeniorityLevel.Unknown || jobLevel == SeniorityLevel.Unknown)
        {
            return 1.0;
        }

        var gap = jobLevel - entryLevel;
        return gap <= 0 ? 1.0 : Math.Pow(LevelGapPenalty, gap);
    }

    private static double ComputeRecency(DateOnly? start, DateOnly? end, DateTimeOffset now)
    {
        if (end is null)
        {
            // Null end date means "present" for an included, dated entry.
            return start is null ? 0.5 : 1.0;
        }

        var monthsAgo = ((now.Year - end.Value.Year) * 12) + now.Month - end.Value.Month;
        if (monthsAgo < 0)
        {
            monthsAgo = 0;
        }

        return Math.Clamp(1.0 - (monthsAgo / RecencyHorizonMonths), 0.0, 1.0);
    }

    private static double Jaccard(IReadOnlyList<string> tags, IReadOnlySet<string> jdSkills)
    {
        if (tags.Count == 0 || jdSkills.Count == 0)
        {
            return 0.0;
        }

        var tagSet = new HashSet<string>(tags, StringComparer.Ordinal);
        var intersection = 0;
        foreach (var tag in tagSet)
        {
            if (jdSkills.Contains(tag))
            {
                intersection++;
            }
        }

        var unionCount = tagSet.Count + jdSkills.Count - intersection;
        return unionCount == 0 ? 0.0 : (double)intersection / unionCount;
    }

    private static HashSet<string> BuildJdSkillSet(JobAnalysis analysis)
    {
        var set = new HashSet<string>(analysis.Keywords, StringComparer.Ordinal);
        foreach (var requirement in analysis.Requirements)
        {
            foreach (var skill in requirement.Skills)
            {
                set.Add(skill);
            }
        }

        return set;
    }

    private static List<string> BuildQueryTerms(JobAnalysis analysis)
    {
        var terms = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var requirement in analysis.Requirements)
        {
            foreach (Match m in WordPattern().Matches(requirement.Text))
            {
                var word = m.Value.ToLowerInvariant();
                if (word.Length >= 3 && !StopWords.Contains(word))
                {
                    terms.Add(word);
                }
            }
        }

        return [.. terms];
    }

    private static Corpus BuildCorpus(IEnumerable<string> texts)
    {
        var docs = texts.Select(TokenizeWords).ToList();
        var avgDocLength = docs.Count == 0 ? 0.0 : docs.Average(d => d.Count);
        var docFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            foreach (var term in new HashSet<string>(doc, StringComparer.Ordinal))
            {
                docFrequency[term] = docFrequency.GetValueOrDefault(term) + 1;
            }
        }

        return new Corpus(docs, avgDocLength, docFrequency);
    }

    private static double Bm25Score(Corpus corpus, int docIndex, IReadOnlyList<string> queryTerms)
    {
        var docCount = corpus.Docs.Count;
        if (docCount == 0)
        {
            return 0.0;
        }

        var doc = corpus.Docs[docIndex];
        if (doc.Count == 0)
        {
            return 0.0;
        }

        var termFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in doc)
        {
            termFrequency[term] = termFrequency.GetValueOrDefault(term) + 1;
        }

        double score = 0.0;

        foreach (var term in queryTerms)
        {
            if (!termFrequency.TryGetValue(term, out var f) || f == 0)
            {
                continue;
            }

            var n = corpus.DocFrequency.GetValueOrDefault(term);
            var idf = Math.Log((((docCount - n + 0.5) / (n + 0.5)) + 1));
            var denominator = f + (K1 * (1 - B + (B * (doc.Count / corpus.AvgDocLength))));
            score += idf * (f * (K1 + 1)) / denominator;
        }

        return score;
    }

    private static List<string> TokenizeWords(string text)
    {
        var result = new List<string>();
        foreach (Match m in WordPattern().Matches(text))
        {
            result.Add(m.Value.ToLowerInvariant());
        }

        return result;
    }

    private static List<ScoredCandidate> SortDeterministic(IEnumerable<ScoredCandidate> candidates) =>
        candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.EntityId, StringComparer.Ordinal)
            .ToList();

    private sealed record Corpus(List<List<string>> Docs, double AvgDocLength, Dictionary<string, int> DocFrequency);

    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
