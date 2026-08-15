using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Autofill;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>EF/SQLite <see cref="ILearnedFieldMapRepository"/>.</summary>
public sealed class LearnedFieldMapRepository(ResumeForgeDbContext dbContext) : ILearnedFieldMapRepository
{
    /// <inheritdoc />
    public async Task<LearnedFieldMap?> GetAsync(string host, string formSignature, CancellationToken ct)
    {
        var entity = await dbContext.LearnedFieldMaps.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Host == host && e.FormSignature == formSignature, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToMap(entity);
    }

    /// <inheritdoc />
    public async Task SaveAsync(LearnedFieldMap map, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);

        var existing = await dbContext.LearnedFieldMaps
            .FirstOrDefaultAsync(e => e.Host == map.Host && e.FormSignature == map.FormSignature, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.LearnedFieldMaps.Add(new LearnedFieldMapEntity
            {
                Host = map.Host,
                FormSignature = map.FormSignature,
                ElementToKey = new Dictionary<string, string>(map.ElementToKey, StringComparer.Ordinal),
                LearnedAt = map.LearnedAt,
                HitCount = map.HitCount,
            });
        }
        else
        {
            existing.ElementToKey = new Dictionary<string, string>(map.ElementToKey, StringComparer.Ordinal);
            existing.LearnedAt = map.LearnedAt;
            existing.HitCount = map.HitCount;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static LearnedFieldMap ToMap(LearnedFieldMapEntity e) => new()
    {
        Host = e.Host,
        FormSignature = e.FormSignature,
        ElementToKey = e.ElementToKey,
        LearnedAt = e.LearnedAt,
        HitCount = e.HitCount,
    };
}
