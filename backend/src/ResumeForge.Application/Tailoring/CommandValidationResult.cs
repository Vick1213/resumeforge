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
    /// <summary>The command that was rejected.</summary>
    public required TailorCommand Command { get; init; }

    /// <summary>Human-readable explanation.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// A stable machine-readable code identifying which rule failed, e.g.
    /// <c>"unknown-target"</c>, <c>"fabricated-metric"</c>.
    /// </summary>
    public required string Code { get; init; }
}
