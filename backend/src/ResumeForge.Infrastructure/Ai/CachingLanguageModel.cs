using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Persistence;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// <see cref="ILanguageModel"/> decorator that caches responses in the database
/// (<see cref="ResumeForgeDbContext.ModelCacheEntries"/>), keyed by the SHA-256 of
/// <c>(ModelId, System, User, SchemaName)</c>. Only requests carrying a non-null
/// <see cref="ModelRequest.CacheKey"/> are cached, per CONTRACTS.md §8.
/// </summary>
public sealed class CachingLanguageModel(ILanguageModel inner, ResumeForgeDbContext dbContext, TimeProvider timeProvider) : ILanguageModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public string ModelId => inner.ModelId;

    /// <inheritdoc />
    public async Task<ModelResponse<T>> CompleteAsync<T>(ModelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CacheKey is null)
        {
            return await inner.CompleteAsync<T>(request, ct).ConfigureAwait(false);
        }

        var key = ComputeKey(inner.ModelId, request);

        var cached = await dbContext.ModelCacheEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct)
            .ConfigureAwait(false);

        if (cached is not null)
        {
            var cachedValue = JsonSerializer.Deserialize<T>(cached.ResponseJson, SerializerOptions)
                ?? throw new InvalidOperationException("Cached model response deserialized to null.");

            return new ModelResponse<T>
            {
                Value = cachedValue,
                Usage = new TokenUsage
                {
                    InputTokens = cached.InputTokens,
                    OutputTokens = cached.OutputTokens,
                    ModelCalls = 0,
                    CacheHits = 1,
                },
                FromCache = true,
            };
        }

        var response = await inner.CompleteAsync<T>(request, ct).ConfigureAwait(false);

        dbContext.ModelCacheEntries.Add(new ModelCacheEntryEntity
        {
            Key = key,
            ModelId = inner.ModelId,
            SchemaName = request.SchemaName,
            ResponseJson = JsonSerializer.Serialize(response.Value, SerializerOptions),
            InputTokens = response.Usage.InputTokens,
            OutputTokens = response.Usage.OutputTokens,
            CreatedAt = timeProvider.GetUtcNow(),
        });

        try
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent request already cached this exact key; the response we just
            // computed is still correct to return, we simply don't persist a duplicate.
        }

        return response;
    }

    private static string ComputeKey(string modelId, ModelRequest request)
    {
        var payload = string.Join('\n', modelId, request.System, request.User, request.SchemaName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
