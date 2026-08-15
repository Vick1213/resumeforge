using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Graph;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>EF/SQLite <see cref="ITailoringRunRepository"/>.</summary>
public sealed class TailoringRunRepository(ResumeForgeDbContext dbContext) : ITailoringRunRepository
{
    /// <inheritdoc />
    public async Task<string> SaveAsync(TailoringRunRecord run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);

        var id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString() : run.Id;
        var existing = await dbContext.TailoringRuns.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.TailoringRuns.Add(new TailoringRunEntity
            {
                Id = id,
                JobId = run.JobId,
                BaseResumeId = run.BaseResumeId,
                Result = run.Result,
                CreatedAt = run.CreatedAt,
            });
        }
        else
        {
            existing.JobId = run.JobId;
            existing.BaseResumeId = run.BaseResumeId;
            existing.Result = run.Result;
            existing.CreatedAt = run.CreatedAt;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc />
    public async Task<TailoringRunRecord?> GetAsync(string runId, CancellationToken ct)
    {
        var entity = await dbContext.TailoringRuns.AsNoTracking().FirstOrDefaultAsync(e => e.Id == runId, ct).ConfigureAwait(false);
        return entity is null
            ? null
            : new TailoringRunRecord
            {
                Id = entity.Id,
                JobId = entity.JobId,
                BaseResumeId = entity.BaseResumeId,
                Result = entity.Result,
                CreatedAt = entity.CreatedAt,
            };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNodeTrace>?> GetTraceAsync(string runId, CancellationToken ct)
    {
        var entity = await dbContext.TailoringRuns.AsNoTracking().FirstOrDefaultAsync(e => e.Id == runId, ct).ConfigureAwait(false);
        return entity?.Result.Trace;
    }
}
