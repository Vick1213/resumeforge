using System.Collections.Concurrent;
using ResumeForge.Application.Abstractions;

namespace ResumeForge.Application.Graph;

/// <summary>
/// Thread-safe <see cref="ITokenBudget"/> backed by a lock-free per-node dictionary and a
/// single lock guarding the running total, so <see cref="Record"/> calls from concurrently
/// executing graph nodes never lose an update.
/// </summary>
public sealed class TokenBudget : ITokenBudget
{
    private readonly ConcurrentDictionary<string, TokenUsage> _perNode = new(StringComparer.Ordinal);
    private readonly Lock _totalLock = new();
    private readonly int? _ceiling;
    private TokenUsage _total = TokenUsage.Empty;

    /// <summary>
    /// Creates a budget with an optional hard <paramref name="ceiling"/> on total
    /// (input + output) tokens. When null, the budget never throws.
    /// </summary>
    public TokenBudget(int? ceiling = null)
    {
        _ceiling = ceiling;
    }

    /// <inheritdoc />
    public void Record(string node, TokenUsage usage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(usage);

        lock (_totalLock)
        {
            var prospective = _total + usage;

            if (_ceiling is { } max && prospective.InputTokens + prospective.OutputTokens > max)
            {
                throw new TokenBudgetExceededException(
                    $"Recording {usage.InputTokens + usage.OutputTokens} token(s) for node '{node}' would bring the " +
                    $"run total to {prospective.InputTokens + prospective.OutputTokens}, exceeding the budget ceiling of {max}.");
            }

            _total = prospective;
        }

        _perNode.AddOrUpdate(node, usage, (_, existing) => existing + usage);
    }

    /// <inheritdoc />
    public TokenUsage Total
    {
        get
        {
            lock (_totalLock)
            {
                return _total;
            }
        }
    }

    /// <inheritdoc />
    public TokenUsage ForNode(string node) => _perNode.TryGetValue(node, out var usage) ? usage : TokenUsage.Empty;
}
