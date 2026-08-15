using ResumeForge.Infrastructure.Knowledge;

namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>An <see cref="IProfileRootProvider"/> test double that always returns a fixed path.</summary>
public sealed class StaticProfileRootProvider(string profileRoot) : IProfileRootProvider
{
    /// <inheritdoc />
    public string ProfileRoot { get; } = profileRoot;
}
