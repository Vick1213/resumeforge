using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>EF/SQLite <see cref="IApplicationRepository"/>.</summary>
public sealed class ApplicationRepository(ResumeForgeDbContext dbContext) : IApplicationRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<JobApplicationRecord>> ListAsync(CancellationToken ct)
    {
        // SQLite cannot translate ORDER BY over a DateTimeOffset column, so the ordering
        // happens client-side after the (small) result set is materialized.
        var entities = await dbContext.Applications.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        return [.. entities.OrderByDescending(e => e.UpdatedAt).Select(ToRecord)];
    }

    /// <inheritdoc />
    public async Task<JobApplicationRecord?> GetAsync(string id, CancellationToken ct)
    {
        var entity = await dbContext.Applications.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<JobApplicationRecord> CreateAsync(JobApplicationRecord application, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(application);

        var id = string.IsNullOrWhiteSpace(application.Id) ? Guid.NewGuid().ToString() : application.Id;
        var entity = new ApplicationEntity
        {
            Id = id,
            JobId = application.JobId,
            ResumeId = application.ResumeId,
            Status = application.Status,
            Notes = application.Notes,
            CreatedAt = application.CreatedAt,
            UpdatedAt = application.UpdatedAt,
        };

        dbContext.Applications.Add(entity);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<JobApplicationRecord?> UpdateAsync(JobApplicationRecord application, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(application);

        var existing = await dbContext.Applications.FirstOrDefaultAsync(e => e.Id == application.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        existing.JobId = application.JobId;
        existing.ResumeId = application.ResumeId;
        existing.Status = application.Status;
        existing.Notes = application.Notes;
        existing.UpdatedAt = application.UpdatedAt;

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToRecord(existing);
    }

    private static JobApplicationRecord ToRecord(ApplicationEntity e) => new()
    {
        Id = e.Id,
        JobId = e.JobId,
        ResumeId = e.ResumeId,
        Status = e.Status,
        Notes = e.Notes,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
