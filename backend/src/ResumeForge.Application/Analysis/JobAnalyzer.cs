using System.Text.RegularExpressions;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Text;

namespace ResumeForge.Application.Analysis;

/// <summary>
/// Deterministic, pure-C# implementation of <see cref="IJobAnalyzer"/>. Performs
/// heading-based section detection, cue-phrase classification, taxonomy-driven skill
/// extraction, and frequency-weighted keyword ranking. No network or model call is ever
/// made from this type.
/// </summary>
public sealed partial class JobAnalyzer(ISkillTaxonomy taxonomy) : IJobAnalyzer
{
    private const int MaxKeywords = 40;
    private const int MaxMissingSkills = 20;

    private static readonly string[] OptionalCues =
    [
        "nice to have", "nice-to-have", "preferred", "bonus", "a plus", "plus if", "would be great",
    ];

    private static readonly string[] MandatoryCues =
    [
        "required", "must have", "must-have", "you have", "you must", "must possess", "need to have",
    ];

    private static readonly string[] EducationCues =
    [
        "degree", "bachelor", "master", "phd", "ph.d", "b.s.", "m.s.", "bs in", "ms in", "university", "diploma",
    ];

    private static readonly string[] ExperienceCues =
    [
        "years of experience", "years experience", "years in", "background in", "track record of", "experience in",
        "experience with",
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "And", "With", "For", "Our", "You", "Your", "We", "This", "That", "Will", "Have", "Are", "Is",
        "In", "On", "Of", "To", "As", "At", "A", "An", "Be", "By", "Or", "It", "Its", "From", "Not", "Can",
        "All", "New", "Team", "Role", "Work", "Working", "About",

        // Generic JD prose that reads as skill-shaped (capitalized, no punctuation) but names
        // no technology or practice on its own — observed junk from real postings, plus
        // obviously-equivalent inflections. Deliberately does NOT include words that name an
        // actual practice even generically (e.g. "containerization").
        "applications", "application", "built", "build", "building", "control", "deployed", "deploy",
        "deployment", "engineering", "foundation", "just", "keep", "maintained", "maintain", "maintaining",
        "production", "running", "run", "runs", "software", "takes", "take", "taking", "understand",
        "understanding", "using", "used", "use", "uses", "ability", "abilities", "experience", "experienced",
        "work", "working", "team", "teams", "strong", "skills", "skill", "tools", "systems", "system",
        "services", "service", "solutions", "solution", "knowledge", "familiarity", "proficiency", "proficient",
        "pipelines", "pipeline", "plus",
    };

    /// <summary>
    /// Canonical taxonomy entries that double as common English verbs describing the
    /// *employer's* own action in ordinary JD boilerplate — "We are hiring a senior
    /// backend engineer" — rather than a skill the candidate is being asked to
    /// demonstrate. Extracting these from that kind of opener pollutes
    /// <see cref="JobAnalysis.MatchedSkills"/> (and, downstream, the emphasizeSkills
    /// target set built from it) with a false match on nearly every posting. Skills whose
    /// canonical form describes something the *candidate* does (mentoring, onboarding,
    /// on-call) are left alone, since those are genuine signals even when phrased as a
    /// verb. Kept separate from <see cref="StopWords"/>, which filters candidate
    /// MissingSkills tokens rather than confirmed taxonomy hits.
    /// </summary>
    private static readonly HashSet<string> NonSkillVerbCanonicals = new(StringComparer.Ordinal)
    {
        "hiring",
    };

    /// <inheritdoc />
    public JobAnalysis Analyze(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);

        var lines = SplitLines(posting.RawText);
        var requirements = new List<Requirement>();
        var weightedSkills = new Dictionary<string, double>(StringComparer.Ordinal);
        var missingSkillCandidates = new SortedSet<string>(StringComparer.Ordinal);

        var section = JdSection.Body;

        foreach (var rawLine in lines)
        {
            var stripped = StripBulletMarker(rawLine).Trim();
            if (stripped.Length == 0)
            {
                continue;
            }

            if (TryDetectHeading(stripped, out var detected))
            {
                section = detected;
                continue;
            }

            if (section == JdSection.Noise)
            {
                continue;
            }

            foreach (var sentence in SplitSentences(stripped))
            {
                var text = CollapseWhitespace(sentence).Trim();
                if (text.Length < 3)
                {
                    continue;
                }

                var isMandatory = ClassifyMandatory(text, section);
                var kind = ClassifyKind(text, section);
                var skills = ExtractSkills(text, section, weightedSkills);

                if (section is JdSection.Requirements or JdSection.Preferred)
                {
                    CollectMissingSkillCandidates(text, skills, missingSkillCandidates);
                }

                requirements.Add(new Requirement
                {
                    Id = $"req:{requirements.Count}",
                    Text = text,
                    Kind = kind,
                    IsMandatory = isMandatory,
                    Skills = skills,
                    Weight = 0, // assigned below, once the final count is known
                });
            }
        }

        requirements = AssignWeights(requirements);

        var keywords = RankKeywords(weightedSkills);
        var matched = keywords.Where(taxonomy.AllCanonical.Contains).ToList();
        var missing = missingSkillCandidates.Take(MaxMissingSkills).ToList();

        return new JobAnalysis
        {
            JobId = posting.Id,
            Requirements = requirements,
            Keywords = keywords,
            MatchedSkills = matched,
            MissingSkills = missing,
            Seniority = DetectSeniority(posting.Title, posting.RawText),
        };
    }

    private static List<Requirement> AssignWeights(List<Requirement> requirements)
    {
        var count = requirements.Count;
        var result = new List<Requirement>(count);

        for (var i = 0; i < count; i++)
        {
            var positionScore = count <= 1 ? 1.0 : 1.0 - (double)i / (count - 1);
            var baseScore = requirements[i].IsMandatory ? 0.7 : 0.35;
            var weight = Math.Clamp(baseScore + (0.3 * positionScore), 0.0, 1.0);
            result.Add(requirements[i] with { Weight = weight });
        }

        return result;
    }

    private static bool ClassifyMandatory(string text, JdSection section)
    {
        if (section == JdSection.Preferred)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();

        foreach (var cue in OptionalCues)
        {
            if (lower.Contains(cue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var cue in MandatoryCues)
        {
            if (lower.Contains(cue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return true;
    }

    private static RequirementKind ClassifyKind(string text, JdSection section)
    {
        var lower = text.ToLowerInvariant();

        foreach (var cue in EducationCues)
        {
            if (lower.Contains(cue, StringComparison.Ordinal))
            {
                return RequirementKind.Education;
            }
        }

        if (YearsPattern().IsMatch(lower))
        {
            return RequirementKind.Experience;
        }

        foreach (var cue in ExperienceCues)
        {
            if (lower.Contains(cue, StringComparison.Ordinal))
            {
                return RequirementKind.Experience;
            }
        }

        if (section == JdSection.Responsibilities)
        {
            return RequirementKind.Responsibility;
        }

        if (section is JdSection.Requirements or JdSection.Preferred)
        {
            return RequirementKind.Skill;
        }

        return RequirementKind.Other;
    }

    private List<string> ExtractSkills(string text, JdSection section, Dictionary<string, double> weightedSkills)
    {
        var words = Tokenize(text);
        var consumed = new bool[words.Count];
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var sectionWeight = SectionWeight(section);

        for (var span = 3; span >= 1; span--)
        {
            for (var i = 0; i + span <= words.Count; i++)
            {
                if (AnyConsumed(consumed, i, span))
                {
                    continue;
                }

                var phrase = string.Join(' ', words.Skip(i).Take(span));
                var normalized = SkillNormalizer.Normalize(phrase);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (taxonomy.TryCanonicalize(normalized, out var canonical) && !NonSkillVerbCanonicals.Contains(canonical))
                {
                    for (var k = 0; k < span; k++)
                    {
                        consumed[i + k] = true;
                    }

                    found.Add(canonical);
                    weightedSkills[canonical] = weightedSkills.GetValueOrDefault(canonical) + sectionWeight;
                    continue;
                }

                // A slash-joined compound is tried whole first (above) — "CI/CD" canonicalizes
                // as a single taxonomy entry that way. But a compound like "git/GitHub" names two
                // distinct known skills shorthand rather than one; when the whole phrase doesn't
                // canonicalize, fall back to trying each slash-delimited part on its own. Only
                // single-word spans are eligible, since '/' never separates space-delimited words.
                if (span == 1 && words[i].Contains('/', StringComparison.Ordinal))
                {
                    var matchedAnyPart = false;

                    foreach (var part in words[i].Split('/', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var partNormalized = SkillNormalizer.Normalize(part);
                        if (partNormalized.Length == 0
                            || !taxonomy.TryCanonicalize(partNormalized, out var partCanonical)
                            || NonSkillVerbCanonicals.Contains(partCanonical))
                        {
                            continue;
                        }

                        found.Add(partCanonical);
                        weightedSkills[partCanonical] = weightedSkills.GetValueOrDefault(partCanonical) + sectionWeight;
                        matchedAnyPart = true;
                    }

                    if (matchedAnyPart)
                    {
                        consumed[i] = true;
                    }
                }
            }
        }

        return [.. found];
    }

    private static void CollectMissingSkillCandidates(string text, IReadOnlyList<string> matchedSkills, SortedSet<string> missing)
    {
        if (missing.Count >= MaxMissingSkills)
        {
            return;
        }

        // SkillLikeTokenPattern doesn't include '/', so a slash-joined compound like "CI/CD"
        // is seen here as two separate tokens ("CI", "CD"). If the whole compound was already
        // resolved to a single taxonomy skill by ExtractSkills (e.g. "cicd"), neither half is a
        // meaningful "unrecognized" candidate on its own — exclude the compound's whole span so
        // its parts never reach the per-token loop below. A compound that ExtractSkills couldn't
        // resolve as a whole (e.g. "TCP/IP") is left alone and its parts fall through to the
        // existing per-token handling, same as before.
        var suppressedRanges = new List<(int Start, int End)>();
        foreach (Match compound in SlashCompoundPattern().Matches(text))
        {
            var normalizedWhole = SkillNormalizer.Normalize(compound.Value);
            if (normalizedWhole.Length > 0 && matchedSkills.Contains(normalizedWhole))
            {
                suppressedRanges.Add((compound.Index, compound.Index + compound.Length));
            }
        }

        foreach (Match match in SkillLikeTokenPattern().Matches(text))
        {
            if (suppressedRanges.Any(r => match.Index >= r.Start && match.Index + match.Length <= r.End))
            {
                continue;
            }

            var word = match.Value;
            if (StopWords.Contains(word))
            {
                continue;
            }

            var normalized = SkillNormalizer.Normalize(word);
            if (normalized.Length < 2)
            {
                continue;
            }

            if (matchedSkills.Contains(normalized))
            {
                continue;
            }

            missing.Add(normalized);
        }
    }

    private List<string> RankKeywords(Dictionary<string, double> weightedSkills)
    {
        return weightedSkills
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxKeywords)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static double SectionWeight(JdSection section) => section switch
    {
        JdSection.Requirements => 2.0,
        JdSection.Preferred => 1.5,
        JdSection.Responsibilities => 1.0,
        _ => 1.0,
    };

    private static SeniorityLevel DetectSeniority(string? title, string rawText)
    {
        var fromTitle = SeniorityClassifier.FromTitle(title);
        var fromYears = DetectSeniorityFromYears(rawText);
        return fromTitle > fromYears ? fromTitle : fromYears;
    }

    private static SeniorityLevel DetectSeniorityFromYears(string rawText)
    {
        var best = SeniorityLevel.Unknown;

        foreach (Match match in YearsPattern().Matches(rawText))
        {
            if (!int.TryParse(match.Groups["years"].Value, out var years))
            {
                continue;
            }

            var level = years switch
            {
                >= 12 => SeniorityLevel.Principal,
                >= 8 => SeniorityLevel.Staff,
                >= 5 => SeniorityLevel.Senior,
                >= 2 => SeniorityLevel.Mid,
                _ => SeniorityLevel.Junior,
            };

            if (level > best)
            {
                best = level;
            }
        }

        return best;
    }

    private static bool AnyConsumed(bool[] consumed, int start, int span)
    {
        for (var i = start; i < start + span; i++)
        {
            if (consumed[i])
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Tokenize(string text)
    {
        var result = new List<string>();

        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = raw.Trim('(', ')', '"', '\'', ',', ';', ':', '!', '?', '.', '[', ']', '{', '}');
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static IEnumerable<string> SplitSentences(string line) =>
        SentenceBoundaryPattern().Split(line).Where(s => s.Trim().Length > 0);

    private static string StripBulletMarker(string line)
    {
        var trimmed = line.TrimStart();
        return BulletMarkerPattern().Replace(trimmed, string.Empty, 1);
    }

    private static string CollapseWhitespace(string text) => WhitespacePattern().Replace(text, " ");

    private static bool TryDetectHeading(string line, out JdSection section)
    {
        var candidate = line.TrimEnd(':', ' ');

        if (RequirementsHeadingPattern().IsMatch(candidate))
        {
            section = JdSection.Requirements;
            return true;
        }

        if (PreferredHeadingPattern().IsMatch(candidate))
        {
            section = JdSection.Preferred;
            return true;
        }

        if (ResponsibilitiesHeadingPattern().IsMatch(candidate))
        {
            section = JdSection.Responsibilities;
            return true;
        }

        if (NoiseHeadingPattern().IsMatch(candidate))
        {
            section = JdSection.Noise;
            return true;
        }

        section = default;
        return false;
    }

    private enum JdSection
    {
        Body,
        Requirements,
        Preferred,
        Responsibilities,
        Noise,
    }

    [GeneratedRegex(@"^[\-\*••]\s*|^\d+[\.\)]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BulletMarkerPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryPattern();

    private static Regex YearsPattern() => YearsPatternNamed();

    [GeneratedRegex(@"(?<years>\d+)\s*\+?\s*years?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex YearsPatternNamed();

    [GeneratedRegex(@"^(requirements?|qualifications?|what you.?ll need|what we.?re looking for|minimum qualifications?|basic qualifications?)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RequirementsHeadingPattern();

    [GeneratedRegex(@"^(nice to have|nice-to-have|preferred( qualifications?)?|bonus( points)?|good to have)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PreferredHeadingPattern();

    [GeneratedRegex(@"^(responsibilities|what you.?ll do|the role|your role|duties|day.to.day)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ResponsibilitiesHeadingPattern();

    [GeneratedRegex(@"^(about( the| this)? (us|company|team)|benefits( ?(&|and) ?perks)?|perks|equal opportunity( employer)?|eeo|compensation( ?(&|and) ?benefits)?|diversity( ?(&|and) ?inclusion)?)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NoiseHeadingPattern();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+#.]*", RegexOptions.CultureInvariant)]
    private static partial Regex SkillLikeTokenPattern();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+#.]*(?:/[A-Za-z][A-Za-z0-9+#.]*)+", RegexOptions.CultureInvariant)]
    private static partial Regex SlashCompoundPattern();
}
