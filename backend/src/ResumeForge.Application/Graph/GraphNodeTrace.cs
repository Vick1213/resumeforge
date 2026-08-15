namespace ResumeForge.Application.Graph;

/// <summary>
/// A single node's outcome from one graph run: its status, how long it took, its token
/// spend, and its error when it failed. The full set of these for a run is returned to
/// the client and rendered as a live pipeline view in the frontend.
/// </summary>
public sealed record GraphNodeTrace
{
    /// <summary>The node's name, as declared to <c>GraphBuilder.AddNode</c>.</summary>
    public required string Node { get; init; }

    /// <summary>The terminal status the node reached.</summary>
    public required GraphNodeStatus Status { get; init; }

    /// <summary>How long the node's body ran, measured with the executor's <see cref="TimeProvider"/>.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>The failure message, when <see cref="Status"/> is <see cref="GraphNodeStatus.Failed"/>, or the skip/cancel reason.</summary>
    public string? Error { get; init; }

    /// <summary>Input tokens this node spent, from <see cref="ITokenBudget"/>.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens this node spent, from <see cref="ITokenBudget"/>.</summary>
    public int OutputTokens { get; init; }
}
