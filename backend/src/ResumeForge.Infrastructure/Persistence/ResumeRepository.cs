using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>
/// EF/SQLite <see cref="IResumeRepository"/>. Base-ness is an explicit flag supplied by the
/// caller on every <see cref="SaveAsync"/> and persisted on <see cref="ResumeEntity.IsBase"/>
/// — the shared <see cref="ResumeDocument"/> contract carries no such field (it crosses
/// component boundaries and CONTRACTS.md does not declare one), so the flag travels
/// alongside the document as a separate parameter instead. Never inferred from
/// <see cref="ResumeDocument.Name"/>, so renaming a resume cannot change which one is base.
/// </summary>
public sealed class ResumeRepository(ResumeForgeDbContext dbContext) : IResumeRepository
{
    /// <inheritdoc />
    public async Task<ResumeDocument?> GetAsync(string id, CancellationToken ct)
    {
        var entity = await dbContext.Resumes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : ToDocument(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeDocument>> ListAsync(CancellationToken ct)
    {
        // SQLite cannot translate ORDER BY over a DateTimeOffset column, so the ordering
        // happens client-side after the (small) result set is materialized.
        var entities = await dbContext.Resumes.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        return [.. entities.OrderByDescending(e => e.UpdatedAt).Select(ToDocument)];
    }

    /// <inheritdoc />
    public async Task<ResumeDocument?> GetBaseAsync(CancellationToken ct)
    {
        var entities = await dbContext.Resumes.AsNoTracking().Where(e => e.IsBase).ToListAsync(ct).ConfigureAwait(false);
        var entity = entities.OrderByDescending(e => e.UpdatedAt).FirstOrDefault();

        return entity is null ? null : ToDocument(entity);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ResumeDocument resume, bool isBase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resume);

        // Enforce "at most one base resume" here, atomically with the upsert below, rather
        // than trusting callers to have cleared the previous base themselves. The clearing
        // update is issued first and awaited on its own (rather than left for the change
        // tracker to order arbitrarily against the upsert), so within the transaction the
        // database never has two rows with IsBase = true at once — which the unique
        // filtered index on ResumeEntity.IsBase (see ResumeForgeDbContext) also enforces as
        // a hard backstop.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (isBase)
        {
            await dbContext.Resumes
                .Where(e => e.IsBase && e.Id != resume.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.IsBase, false), ct)
                .ConfigureAwait(false);
        }

        var existing = await dbContext.Resumes.FirstOrDefaultAsync(e => e.Id == resume.Id, ct).ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.Resumes.Add(ToEntity(resume, isBase));
        }
        else
        {
            ApplyTo(existing, resume, isBase);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static ResumeDocument ToDocument(ResumeEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Basics = e.Basics,
        Summary = e.Summary,
        Skills = e.Skills,
        Experience = e.Experience,
        Projects = e.Projects,
        Education = e.Education,
        Certifications = e.Certifications,
        SectionOrder = e.SectionOrder,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static ResumeEntity ToEntity(ResumeDocument d, bool isBase) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Basics = d.Basics,
        Summary = d.Summary,
        Skills = [.. d.Skills],
        Experience = [.. d.Experience],
        Projects = [.. d.Projects],
        Education = [.. d.Education],
        Certifications = [.. d.Certifications],
        SectionOrder = [.. d.SectionOrder],
        IsBase = isBase,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    private static void ApplyTo(ResumeEntity entity, ResumeDocument d, bool isBase)
    {
        entity.Name = d.Name;
        entity.Basics = d.Basics;
        entity.Summary = d.Summary;
        entity.Skills = [.. d.Skills];
        entity.Experience = [.. d.Experience];
        entity.Projects = [.. d.Projects];
        entity.Education = [.. d.Education];
        entity.Certifications = [.. d.Certifications];
        entity.SectionOrder = [.. d.SectionOrder];
        entity.IsBase = isBase;
        entity.CreatedAt = d.CreatedAt;
        entity.UpdatedAt = d.UpdatedAt;
    }
}
