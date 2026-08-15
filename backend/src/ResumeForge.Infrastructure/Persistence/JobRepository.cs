using Microsoft.EntityFrameworkCore;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>EF/SQLite <see cref="IJobRepository"/>.</summary>
public sealed class JobRepository(ResumeForgeDbContext dbContext) : IJobRepository
{
    /// <inheritdoc />
    public async Task<JobPosting?> GetAsync(string id, CancellationToken ct)
    {
        var entity = await dbContext.JobPostings.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : ToPosting(entity);
    }

    /// <inheritdoc />
    public async Task SaveAsync(JobPosting posting, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(posting);

        var existing = await dbContext.JobPostings.FirstOrDefaultAsync(e => e.Id == posting.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            dbContext.JobPostings.Add(ToEntity(posting));
        }
        else
        {
            existing.SourceUrl = posting.SourceUrl;
            existing.Company = posting.Company;
            existing.Title = posting.Title;
            existing.Location = posting.Location;
            existing.RawText = posting.RawText;
            existing.FetchedAt = posting.FetchedAt;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobAnalysis?> GetAnalysisAsync(string jobId, CancellationToken ct)
    {
        var entity = await dbContext.JobAnalyses.AsNoTracking().FirstOrDefaultAsync(e => e.JobId == jobId, ct).ConfigureAwait(false);
        return entity is null ? null : ToAnalysis(entity);
    }

    /// <inheritdoc />
    public async Task SaveAnalysisAsync(JobAnalysis analysis, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var existing = await dbContext.JobAnalyses.FirstOrDefaultAsync(e => e.JobId == analysis.JobId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            dbContext.JobAnalyses.Add(ToEntity(analysis));
        }
        else
        {
            existing.Requirements = [.. analysis.Requirements];
            existing.Keywords = [.. analysis.Keywords];
            existing.MatchedSkills = [.. analysis.MatchedSkills];
            existing.MissingSkills = [.. analysis.MissingSkills];
            existing.Seniority = analysis.Seniority;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static JobPosting ToPosting(JobPostingEntity e) => new()
    {
        Id = e.Id,
        SourceUrl = e.SourceUrl,
        Company = e.Company,
        Title = e.Title,
        Location = e.Location,
        RawText = e.RawText,
        FetchedAt = e.FetchedAt,
    };

    private static JobPostingEntity ToEntity(JobPosting p) => new()
    {
        Id = p.Id,
        SourceUrl = p.SourceUrl,
        Company = p.Company,
        Title = p.Title,
        Location = p.Location,
        RawText = p.RawText,
        FetchedAt = p.FetchedAt,
    };

    private static JobAnalysis ToAnalysis(JobAnalysisEntity e) => new()
    {
        JobId = e.JobId,
        Requirements = e.Requirements,
        Keywords = e.Keywords,
        MatchedSkills = e.MatchedSkills,
        MissingSkills = e.MissingSkills,
        Seniority = e.Seniority,
    };

    private static JobAnalysisEntity ToEntity(JobAnalysis a) => new()
    {
        JobId = a.JobId,
        Requirements = [.. a.Requirements],
        Keywords = [.. a.Keywords],
        MatchedSkills = [.. a.MatchedSkills],
        MissingSkills = [.. a.MissingSkills],
        Seniority = a.Seniority,
    };
}
