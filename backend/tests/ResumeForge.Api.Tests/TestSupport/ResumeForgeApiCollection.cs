using Xunit;

namespace ResumeForge.Api.Tests.TestSupport;

/// <summary>
/// Groups every endpoint test class onto one shared <see cref="ResumeForgeApiFactory"/>
/// (one host, one temp database, one temp profile directory for the whole run) and, by
/// xUnit's default collection semantics, runs their tests sequentially — several of them
/// mutate the shared database and knowledge base, so parallel execution would race.
/// </summary>
[CollectionDefinition("ResumeForgeApi")]
public sealed class ResumeForgeApiCollection : ICollectionFixture<ResumeForgeApiFactory>
{
}
