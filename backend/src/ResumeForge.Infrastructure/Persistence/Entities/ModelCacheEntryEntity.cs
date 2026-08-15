namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity backing the <c>ModelCacheEntries</c> table that
/// <see cref="ResumeForge.Infrastructure.Ai.CachingLanguageModel"/> reads and writes.
/// </summary>
public sealed class ModelCacheEntryEntity
{
    /// <summary>Primary key: the SHA-256 hex digest of <c>(ModelId, System, User, SchemaName)</c>.</summary>
    public required string Key { get; set; }

    public required string ModelId { get; set; }

    public required string SchemaName { get; set; }

    /// <summary>The cached response value, already serialized as JSON.</summary>
    public required string ResponseJson { get; set; }

    public required int InputTokens { get; set; }

    public required int OutputTokens { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
