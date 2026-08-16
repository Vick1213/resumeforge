namespace ResumeForge.Application.Tailoring;

/// <summary>The result of validating a batch of proposed <see cref="TailorCommand"/>s.</summary>
public sealed record CommandValidationResult
{
    /// <summary>Commands that passed every validation rule, in their original order.</summary>
    public required IReadOnlyList<TailorCommand> Accepted { get; init; }

    /// <summary>Commands rejected by at least one rule, each with its reason and stable code.</summary>
    public required IReadOnlyList<RejectedCommand> Rejected { get; init; }
}

/// <summary>A single command that failed validation.</summary>
public sealed record RejectedCommand
{
    /// <summary>
    /// The command that was rejected, or <c>null</c> for <c>Code == "malformed-command"</c>:
    /// an element the model sent never deserialized into a <see cref="TailorCommand"/> in the
    /// first place, so there is no instance to carry here — see
    /// <see cref="TailorCommandParseResult.Malformed"/>, whose <c>Error</c> is folded into
    /// <see cref="Reason"/> instead. Deliberately not <c>required</c> — unlike every other
    /// member here, this one is allowed to be genuinely absent, and the API host's
    /// <c>JsonIgnoreCondition.WhenWritingNull</c> already omits a null property from the wire
    /// entirely, which a <c>required</c> member would then fail to round-trip back in.
    /// </summary>
    public TailorCommand? Command { get; init; }

    /// <summary>Human-readable explanation.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// A stable machine-readable code identifying which rule failed, e.g.
    /// <c>"unknown-target"</c>, <c>"fabricated-metric"</c>, <c>"malformed-command"</c>.
    /// </summary>
    public required string Code { get; init; }
}
