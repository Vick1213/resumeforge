using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;
using ResumeForge.Domain.Text;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="ICommandValidator"/> implementing the six validation rules
/// from CONTRACTS.md §6:
/// <list type="number">
/// <item>Every target/parent/order entry resolves to an existing node (the literal
/// <c>"root"</c> is a valid <see cref="OrderCommand.Parent"/> that names no single node).</item>
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
/// </list>
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

                break;

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

            case SetSummaryCommand:
            case EmphasizeSkillsCommand:
            case SetSectionOrderCommand:
                // No target-resolution rule applies to these per CONTRACTS.md §6.
                break;
        }

        rejection = null!;
        return false;
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
