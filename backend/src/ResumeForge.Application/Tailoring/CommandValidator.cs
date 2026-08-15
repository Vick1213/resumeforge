using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic <see cref="ICommandValidator"/> implementing the five validation rules
/// from CONTRACTS.md §6:
/// <list type="number">
/// <item>Every target/parent/order entry resolves to an existing node (the literal
/// <c>"root"</c> is a valid <see cref="OrderCommand.Parent"/> that names no single node).</item>
/// <item><see cref="SelectVariantCommand.VariantIndex"/> is within range.</item>
/// <item><see cref="RewriteCommand.Text"/> is ≤300 characters, single-line, and passes
/// <see cref="IFabricationGuard"/>.</item>
/// <item>Total accepted <see cref="RewriteCommand"/>s does not exceed <see cref="TailorOptions.MaxRewrites"/>.</item>
/// <item><see cref="OrderCommand.Order"/> contains no duplicates.</item>
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
            if (TryGetRejection(command, document, out var rejection))
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

    private bool TryGetRejection(TailorCommand command, ResumeDocument document, out RejectedCommand rejection)
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

            case SetSummaryCommand:
            case EmphasizeSkillsCommand:
            case SetSectionOrderCommand:
                // No target-resolution rule applies to these per CONTRACTS.md §6.
                break;
        }

        rejection = null!;
        return false;
    }

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
