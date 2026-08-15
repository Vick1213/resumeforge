namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>A <see cref="TimeProvider"/> test double whose "now" is set explicitly, for deterministic tests.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Sets "now" to <paramref name="value"/>.</summary>
    public void Set(DateTimeOffset value) => _now = value;
}
