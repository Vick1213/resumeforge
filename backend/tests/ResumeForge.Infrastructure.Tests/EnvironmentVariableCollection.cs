using Xunit;

namespace ResumeForge.Infrastructure.Tests;

/// <summary>
/// Groups the test classes that read or write provider API-key environment variables.
/// Environment variables are process-global, so xUnit's default parallelism lets one class
/// observe a value another class set and then restored — a race that fails intermittently
/// and passes on a re-run, which is the worst shape a test failure can take. Membership of a
/// single collection serializes them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvironmentVariableCollection
{
    /// <summary>The collection name to put on every class touching these variables.</summary>
    public const string Name = "environment-variables";
}
