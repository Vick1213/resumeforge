using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;
using ResumeForge.Domain.Text;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="ICommandValidator"/> implementing the eight validation rules
/// from CONTRACTS.md §6:
/// <list type="number">
/// <item>Every target/parent/order entry resolves to an existing node (the literal
/// <c>"root"</c> is a valid <see cref="OrderCommand.Parent"/> that names no single node).
/// An <see cref="ExcludeCommand"/> additionally may not target a whole experience entry
/// (code <c>experience-protected</c>): the model may trim a bullet, never a job — only the
/// user, via <see cref="TailorOptions.ExcludedEntryIds"/>, removes employment history.</item>
/// <item><see cref="SelectVariantCommand.VariantIndex"/> is within range.</item>
/// <item><see cref="RewriteCommand.Text"/> is ≤300 characters, single-line, and passes
/// <see cref="IFabricationGuard"/>.</item>
/// <item>Total accepted <see cref="RewriteCommand"/>s does not exceed <see cref="TailorOptions.MaxRewrites"/>.</item>
/// <item><see cref="OrderCommand.Order"/> contains no duplicates.</item>
/// <item><see cref="InjectKeywordsCommand"/> passes rule 3's fabrication guard and every
/// keyword it names is already evidenced in a skill group or in the text of some entry or
/// bullet (code <c>unsupported-keyword</c> otherwise); it is additionally rejected with
/// <c>op-unavailable-at-effort</c> below <see cref="ModelEffort.Thorough"/>. This rule
/// never relaxes at any effort level.</item>
/// <item>Replacement text — a <see cref="RewriteCommand"/>'s or an
/// <see cref="InjectKeywordsCommand"/>'s — is not ragged: it either fits one line or fills
/// its last one to <see cref="BulletLineFit.MinLastLineFill"/> (code <c>ragged-line-fill</c>).
/// A rewrite is only worth accepting if it is an improvement in every respect, and a bullet
/// trailing three words onto a line of their own is not one.</item>
/// <item>A <see cref="SetSummaryCommand"/> states no figure the document does not already
/// evidence (code <c>fabricated-metric</c>). The summary is the only block written as free
/// prose rather than as an edit to existing text, and so the only one rule 3's guard never
/// saw.</item>
/// </list>
/// The <see cref="Validate(IReadOnlyList{TailorCommandParseResult}, ResumeDocument, TailorOptions)"/>
/// overload additionally rejects, with code <c>malformed-command</c>, any array element the
/// model sent that never deserialized into a <see cref="TailorCommand"/> at all — see
/// <see cref="TailorCommandParseResult"/>.
/// </summary>
public sealed class CommandValidator(IFabricationGuard fabricationGuard) : ICommandValidator
{
    private const string RootParent = "root";

    /// <inheritdoc />
    public CommandValidationResult Validate(IReadOnlyList<TailorCommand> commands, ResumeDocument document, TailorOptions options)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var rejected = new List<RejectedCommand>();
        var accepted = new List<TailorCommand>();

        foreach (var command in commands)
        {
            if (TryGetRejection(command, document, options, out var rejection))
            {
                rejected.Add(rejection);
            }
            else
            {
                accepted.Add(command);
            }
        }

        ApplyRewriteLimit(accepted, rejected, options);

        return new CommandValidationResult { Accepted = accepted, Rejected = rejected };
    }

    /// <inheritdoc />
    public CommandValidationResult Validate(IReadOnlyList<TailorCommandParseResult> parseResults, ResumeDocument document, TailorOptions options)
    {
        ArgumentNullException.ThrowIfNull(parseResults);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var malformed = new List<RejectedCommand>();
        var parsed = new List<TailorCommand>();

        foreach (var result in parseResults)
        {
            switch (result)
            {
                case TailorCommandParseResult.Parsed p:
                    parsed.Add(p.Command);
                    break;

                case TailorCommandParseResult.Malformed m:
                    malformed.Add(new RejectedCommand
                    {
                        Command = null,
                        Reason = $"Command at index {m.Index} could not be parsed: {m.Error}",
                        Code = "malformed-command",
                    });
                    break;
            }
        }

        var validated = Validate(parsed, document, options);

        return malformed.Count == 0
            ? validated
            : validated with { Rejected = [.. malformed, .. validated.Rejected] };
    }

    private static void ApplyRewriteLimit(List<TailorCommand> accepted, List<RejectedCommand> rejected, TailorOptions options)
    {
        var rewriteIndices = new List<int>();
        for (var i = 0; i < accepted.Count; i++)
        {
            if (accepted[i] is RewriteCommand)
            {
                rewriteIndices.Add(i);
            }
        }

        if (rewriteIndices.Count <= options.MaxRewrites)
        {
            return;
        }

        var excessIndices = new HashSet<int>(rewriteIndices.Skip(options.MaxRewrites));
        var kept = new List<TailorCommand>(accepted.Count - excessIndices.Count);

        for (var i = 0; i < accepted.Count; i++)
        {
            if (excessIndices.Contains(i))
            {
                rejected.Add(new RejectedCommand
                {
                    Command = accepted[i],
                    Reason = $"Exceeds the maximum of {options.MaxRewrites} rewrite command(s) allowed per run.",
                    Code = "rewrite-limit-exceeded",
                });
            }
            else
            {
                kept.Add(accepted[i]);
            }
        }

        accepted.Clear();
        accepted.AddRange(kept);
    }

    private bool TryGetRejection(TailorCommand command, ResumeDocument document, TailorOptions options, out RejectedCommand rejection)
    {
        switch (command)
        {
            case IncludeCommand include:
                foreach (var target in include.Targets)
                {
                    if (!ResolvesToNode(target, document, out var reason))
                    {
                        rejection = Reject(command, reason, "unknown-target");
                        return true;
                    }
                }

                break;

            case ExcludeCommand exclude:
                foreach (var target in exclude.Targets)
                {
                    if (!ResolvesToNode(target, document, out var reason))
                    {
                        rejection = Reject(command, reason, "unknown-target");
                        return true;
                    }

                    // Employment history is the substance of a resume: the model may trim a
                    // weak bullet, but never a whole job. Dropping one leaves a gap in the
                    // candidate's history that reads worse than any irrelevant entry would.
                    // Only the user, via TailorOptions.ExcludedEntryIds, removes a job.
                    if (EntityId.TryParse(target, out var excludeId) &&
                        excludeId.Kind == EntityKind.Experience &&
                        excludeId.Ordinal is null && excludeId.SubKey is null)
                    {
                        rejection = Reject(
                            command,
                            $"'{target}' is an experience entry; experience entries are never excluded by the model. " +
                            "Exclude an individual bullet instead if it is irrelevant.",
                            "experience-protected");
                        return true;
                    }
                }

                break;

            case OrderCommand order:
                if (!string.Equals(order.Parent, RootParent, StringComparison.Ordinal) &&
                    !ResolvesToNode(order.Parent, document, out var parentReason))
                {
                    rejection = Reject(command, parentReason, "unknown-target");
                    return true;
                }

                foreach (var child in order.Order)
                {
                    if (!ResolvesToNode(child, document, out var childReason))
                    {
                        rejection = Reject(command, childReason, "unknown-target");
                        return true;
                    }
                }

                if (order.Order.Count != new HashSet<string>(order.Order, StringComparer.Ordinal).Count)
                {
                    rejection = Reject(command, $"Order for parent '{order.Parent}' contains duplicate id(s).", "duplicate-order-entry");
                    return true;
                }

                break;

            case SelectVariantCommand selectVariant:
                if (!EntityId.TryParse(selectVariant.Target, out var svId) || !document.TryFindBullet(svId, out var svBullet))
                {
                    rejection = Reject(command, $"'{selectVariant.Target}' does not resolve to a bullet in the document.", "unknown-target");
                    return true;
                }

                if (selectVariant.VariantIndex < 0 || selectVariant.VariantIndex >= svBullet.Variants.Count)
                {
                    rejection = Reject(
                        command,
                        $"Variant index {selectVariant.VariantIndex} is out of range for '{selectVariant.Target}' ({svBullet.Variants.Count} variant(s) available).",
                        "variant-index-out-of-range");
                    return true;
                }

                break;

            case RewriteCommand rewrite:
                if (!EntityId.TryParse(rewrite.Target, out var rwId) || !document.TryFindBullet(rwId, out var original))
                {
                    rejection = Reject(command, $"'{rewrite.Target}' does not resolve to a bullet in the document.", "unknown-target");
                    return true;
                }

                if (rewrite.Text.Length > 300)
                {
                    rejection = Reject(command, $"Rewrite text is {rewrite.Text.Length} characters, exceeding the 300-character limit.", "rewrite-too-long");
                    return true;
                }

                if (rewrite.Text.Contains('\n') || rewrite.Text.Contains('\r'))
                {
                    rejection = Reject(command, "Rewrite text must be a single line.", "rewrite-multiline");
                    return true;
                }

                if (!fabricationGuard.IsSafe(original.Text, rewrite.Text, out var fabricationReason))
                {
                    rejection = Reject(command, fabricationReason ?? "Rewrite failed the anti-fabrication check.", "fabricated-metric");
                    return true;
                }

                if (TryRejectRaggedText(command, rewrite.Text, "Rewrite", out rejection))
                {
                    return true;
                }

                break;

            case SetTaglineCommand setTagline:
            {
                if (options.Effort < ModelEffort.Full)
                {
                    rejection = Reject(
                        command,
                        $"setTagline requires Full effort; this run is {options.Effort}.",
                        "op-unavailable-at-effort");
                    return true;
                }

                var project = document.Projects.FirstOrDefault(p => p.Id == setTagline.Target);
                if (project is null)
                {
                    rejection = Reject(
                        command,
                        $"'{setTagline.Target}' does not resolve to a project in the document.",
                        "unknown-target");
                    return true;
                }

                if (setTagline.Text.Length > 300)
                {
                    rejection = Reject(
                        command,
                        $"Tagline text is {setTagline.Text.Length} characters, exceeding the 300-character limit.",
                        "rewrite-too-long");
                    return true;
                }

                if (setTagline.Text.Contains('\n') || setTagline.Text.Contains('\r'))
                {
                    rejection = Reject(command, "Tagline text must be a single line.", "rewrite-multiline");
                    return true;
                }

                // Guarded against the tagline it replaces, exactly as a bullet rewrite is
                // guarded against the bullet it replaces. A project with no tagline yet has
                // nothing to contradict, so the empty string is the honest baseline.
                if (!fabricationGuard.IsSafe(project.Tagline ?? string.Empty, setTagline.Text, out var taglineReason))
                {
                    rejection = Reject(command, taglineReason ?? "Tagline failed the anti-fabrication check.", "fabricated-metric");
                    return true;
                }

                break;
            }

            case InjectKeywordsCommand inject:
                if (!EntityId.TryParse(inject.Target, out var injId) || !document.TryFindBullet(injId, out var injBullet))
                {
                    rejection = Reject(command, $"'{inject.Target}' does not resolve to a bullet in the document.", "unknown-target");
                    return true;
                }

                // Not negotiable at any effort level (CONTRACTS.md §6): this reuses rule 3's
                // fabrication guard exactly as RewriteCommand does, since inject.Text is a
                // full replacement bullet just like a rewrite's.
                if (!fabricationGuard.IsSafe(injBullet.Text, inject.Text, out var injFabricationReason))
                {
                    rejection = Reject(command, injFabricationReason ?? "InjectKeywords failed the anti-fabrication check.", "fabricated-metric");
                    return true;
                }

                if (TryRejectRaggedText(command, inject.Text, "InjectKeywords text", out rejection))
                {
                    return true;
                }

                if (TryFindUnsupportedKeyword(inject.Keywords, document, out var unsupportedKeyword))
                {
                    rejection = Reject(
                        command,
                        $"'{unsupportedKeyword}' is not evidenced anywhere in the knowledge base — not in a skill group, and not in the text of any entry or bullet.",
                        "unsupported-keyword");
                    return true;
                }

                if (options.Effort < ModelEffort.Thorough)
                {
                    rejection = Reject(
                        command,
                        $"injectKeywords requires at least Thorough effort; this run is {options.Effort}.",
                        "op-unavailable-at-effort");
                    return true;
                }

                break;

            case SetSummaryCommand setSummary:
                if (TryFindUnevidencedNumber(setSummary.Text, document, out var unevidenced))
                {
                    rejection = Reject(
                        command,
                        $"Summary states '{unevidenced}', which appears nowhere in the knowledge base. " +
                        "A summary may restate a figure the resume already earns; it may not introduce one.",
                        "fabricated-metric");
                    return true;
                }

                break;

            case EmphasizeSkillsCommand:
            case SetSectionOrderCommand:
                // No target-resolution rule applies to these per CONTRACTS.md §6.
                break;
        }

        rejection = null!;
        return false;
    }

    /// <summary>
    /// Rule 8's summary-evidence check: every number-bearing token in a proposed summary must
    /// already appear somewhere in the document.
    /// </summary>
    /// <remarks>
    /// The summary is the one block the model writes as free prose rather than as an edit to
    /// something that already exists, which left it the only text in the system with no
    /// anti-fabrication check on it at all — and it is the first thing a human reads. It is
    /// checked against the whole document rather than against the previous summary, because a
    /// good tailored summary legitimately lifts a figure out of a bullet ("saving ~$40K in
    /// operational costs"); what it may not do is invent one, and "2+ years of experience" is
    /// exactly the kind of claim that arrives from nowhere and is trivially disproved by the
    /// dates printed underneath it.
    /// </remarks>
    private static bool TryFindUnevidencedNumber(string summary, ResumeDocument document, out string? unevidenced)
    {
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in EnumerateDocumentText(document))
        {
            foreach (var token in NumberTokens(text))
            {
                evidence.Add(token);
            }
        }

        foreach (var token in NumberTokens(summary))
        {
            if (!evidence.Contains(token))
            {
                unevidenced = token;
                return true;
            }
        }

        unevidenced = null;
        return false;
    }

    /// <summary>
    /// Number-bearing tokens in <paramref name="text"/>, normalized the same way
    /// <see cref="FabricationGuard"/> normalizes them so the two checks agree on what counts
    /// as the same figure written differently.
    /// </summary>
    private static IEnumerable<string> NumberTokens(string text)
    {
        foreach (var rawWord in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = rawWord.Trim('(', ')', '"', '\'', ',', ';', ':', '!', '?', '[', ']', '{', '}');
            if (trimmed.Length == 0 || !trimmed.Any(char.IsAsciiDigit))
            {
                continue;
            }

            yield return trimmed
                .TrimEnd('.', ',', ';', ':', ')', '!', '?')
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace("$", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }

    /// <summary>Every piece of authored text in <paramref name="document"/> a summary may draw on.</summary>
    private static IEnumerable<string> EnumerateDocumentText(ResumeDocument document)
    {
        if (document.Summary is { } summary)
        {
            yield return summary;
        }

        foreach (var entry in document.Experience)
        {
            yield return $"{entry.Role} {entry.Organization}";
            foreach (var bullet in entry.Bullets)
            {
                yield return bullet.Text;
                foreach (var variant in bullet.Variants)
                {
                    yield return variant;
                }
            }
        }

        foreach (var entry in document.Projects)
        {
            yield return $"{entry.Name} {entry.Tagline}";
            foreach (var bullet in entry.Bullets)
            {
                yield return bullet.Text;
                foreach (var variant in bullet.Variants)
                {
                    yield return variant;
                }
            }
        }

        foreach (var entry in document.Education)
        {
            yield return $"{entry.Credential} {entry.Institution} {entry.Gpa}";
            foreach (var highlight in entry.Highlights)
            {
                yield return highlight;
            }
        }

        foreach (var entry in document.Certifications)
        {
            yield return $"{entry.Name} {entry.Issuer}";
        }

        foreach (var group in document.Skills)
        {
            foreach (var skill in group.Items)
            {
                yield return skill.Name;
            }
        }
    }

    /// <summary>
    /// Rule 7's line-fill check. Text that wraps and then leaves its last line less than
    /// <see cref="BulletLineFit.MinLastLineFill"/> full is rejected: the fragment reads as an
    /// unedited line and costs the vertical space of a full one while carrying two or three
    /// words. The rejection is safe because it is a rejection — the bullet the model was
    /// replacing stays exactly as authored — so the worst case of a model that cannot hit the
    /// target is the original text, never a ragged one.
    /// </summary>
    private static bool TryRejectRaggedText(TailorCommand command, string text, string label, out RejectedCommand rejection)
    {
        if (!BulletLineFit.IsRagged(text))
        {
            rejection = null!;
            return false;
        }

        var lines = BulletLineFit.LineCount(text);
        var (min, max) = BulletLineFit.TargetLength(lines - 1);
        var (nextMin, nextMax) = BulletLineFit.TargetLength(lines);

        rejection = Reject(
            command,
            $"{label} is {text.Length} characters, which wraps onto {lines} lines and leaves the last " +
            $"{BulletLineFit.LastLineFill(text):P0} full. Write it to {min}-{max} characters or {nextMin}-{nextMax} characters.",
            "ragged-line-fill");
        return true;
    }

    /// <summary>
    /// Rule 6's KB-evidence check. A keyword is evidenced when some skill in some skill
    /// group normalizes to it exactly, or when it appears — after the same punctuation- and
    /// whitespace-stripping normalization <see cref="SkillNormalizer"/> uses everywhere else
    /// in the system — as a substring of the text of some entry (role, organization, project
    /// name/tagline, institution, credential, certification name/issuer) or some bullet
    /// (including its variants). <paramref name="document"/> is the freshly built base
    /// resume, which — before any command has executed — is a complete, unfiltered
    /// projection of the knowledge base, so searching it is equivalent to searching the KB
    /// itself.
    /// </summary>
    private static bool TryFindUnsupportedKeyword(IReadOnlyList<string> keywords, ResumeDocument document, out string? unsupported)
    {
        foreach (var keyword in keywords)
        {
            if (!IsKeywordEvidenced(keyword, document))
            {
                unsupported = keyword;
                return true;
            }
        }

        unsupported = null;
        return false;
    }

    private static bool IsKeywordEvidenced(string keyword, ResumeDocument document)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        foreach (var group in document.Skills)
        {
            foreach (var skill in group.Items)
            {
                if (string.Equals(skill.Normalized, keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        foreach (var entry in document.Experience)
        {
            if (TextEvidences(entry.Role, keyword) || TextEvidences(entry.Organization, keyword))
            {
                return true;
            }

            if (BulletsEvidence(entry.Bullets, keyword))
            {
                return true;
            }
        }

        foreach (var entry in document.Projects)
        {
            if (TextEvidences(entry.Name, keyword) || TextEvidences(entry.Tagline, keyword))
            {
                return true;
            }

            if (BulletsEvidence(entry.Bullets, keyword))
            {
                return true;
            }
        }

        foreach (var entry in document.Education)
        {
            if (TextEvidences(entry.Institution, keyword) || TextEvidences(entry.Credential, keyword))
            {
                return true;
            }

            foreach (var highlight in entry.Highlights)
            {
                if (TextEvidences(highlight, keyword))
                {
                    return true;
                }
            }
        }

        foreach (var entry in document.Certifications)
        {
            if (TextEvidences(entry.Name, keyword) || TextEvidences(entry.Issuer, keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BulletsEvidence(IReadOnlyList<Bullet> bullets, string keyword)
    {
        foreach (var bullet in bullets)
        {
            if (TextEvidences(bullet.Text, keyword))
            {
                return true;
            }

            foreach (var variant in bullet.Variants)
            {
                if (TextEvidences(variant, keyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TextEvidences(string? text, string normalizedKeyword) =>
        !string.IsNullOrWhiteSpace(text) && SkillNormalizer.Normalize(text).Contains(normalizedKeyword, StringComparison.Ordinal);

    private static RejectedCommand Reject(TailorCommand command, string reason, string code) =>
        new() { Command = command, Reason = reason, Code = code };

    private static bool ResolvesToNode(string idText, ResumeDocument document, out string reason)
    {
        if (!EntityId.TryParse(idText, out var id))
        {
            reason = $"'{idText}' is not a valid entity id.";
            return false;
        }

        if (!document.ContainsNode(id))
        {
            reason = $"'{idText}' does not resolve to a node in the document.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
