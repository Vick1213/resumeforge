using ResumeForge.Application.Abstractions;

namespace ResumeForge.Application.Graph;

/// <summary>
/// Thread-safe accumulator for token usage across the nodes of one graph run, with an
/// optional hard ceiling.
/// </summary>
public interface ITokenBudget
{
    /// <summary>Records <paramref name="usage"/> as spent by <paramref name="node"/>.</summary>
    /// <exception cref="TokenBudgetExceededException">
    /// Thrown when a configured ceiling would be exceeded by this recording.
    /// </exception>
    void Record(string node, TokenUsage usage);

    /// <summary>The running total across every node recorded so far.</summary>
    TokenUsage Total { get; }

    /// <summary>The running total for a single node, or <see cref="TokenUsage.Empty"/> if it has recorded nothing.</summary>
    TokenUsage ForNode(string node);
}
