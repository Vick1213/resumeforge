using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;
using ResumeForge.Infrastructure.Persistence.Entities;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 / SQLite database context described in CONTRACTS.md §11. The markdown files
/// under <c>profile/</c> remain the source of truth for knowledge items; this context
/// holds only derived and operational state: resumes, job postings and their analyses,
/// tailoring runs, the model response cache, learned autofill field maps, and the
/// application tracker.
/// </summary>
public sealed class ResumeForgeDbContext(DbContextOptions<ResumeForgeDbContext> options) : DbContext(options)
{
    /// <summary>The base resume and every tailored variant.</summary>
    public DbSet<ResumeEntity> Resumes => Set<ResumeEntity>();

    /// <summary>Fetched job postings.</summary>
    public DbSet<JobPostingEntity> JobPostings => Set<JobPostingEntity>();

    /// <summary>Deterministic analyses of job postings.</summary>
    public DbSet<JobAnalysisEntity> JobAnalyses => Set<JobAnalysisEntity>();

    /// <summary>Completed tailoring runs.</summary>
    public DbSet<TailoringRunEntity> TailoringRuns => Set<TailoringRunEntity>();

    /// <summary>Cached language model responses, keyed by prompt hash.</summary>
    public DbSet<ModelCacheEntryEntity> ModelCacheEntries => Set<ModelCacheEntryEntity>();

    /// <summary>Learned autofill field maps, keyed by host and form signature.</summary>
    public DbSet<LearnedFieldMapEntity> LearnedFieldMaps => Set<LearnedFieldMapEntity>();

    /// <summary>Tracked job applications.</summary>
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ResumeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // A hard backstop for "at most one base resume": ResumeRepository.SaveAsync
            // clears every other row's flag before setting a new one in the same
            // transaction, but the filtered unique index means the invariant holds even if
            // a future caller bypasses that logic (e.g. a raw SQL fix-up).
            entity.HasIndex(e => e.IsBase).IsUnique().HasFilter("\"IsBase\" = 1");
            entity.HasIndex(e => e.UpdatedAt);

            entity.Property(e => e.Basics).HasConversion(JsonColumn.Converter<ResumeBasics>());
            ConfigureJsonList<SkillGroup>(entity.Property(e => e.Skills));
            ConfigureJsonList<ExperienceEntry>(entity.Property(e => e.Experience));
            ConfigureJsonList<ProjectEntry>(entity.Property(e => e.Projects));
            ConfigureJsonList<EducationEntry>(entity.Property(e => e.Education));
            ConfigureJsonList<CertificationEntry>(entity.Property(e => e.Certifications));
            ConfigureJsonList<SectionKind>(entity.Property(e => e.SectionOrder));
        });

        modelBuilder.Entity<JobPostingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<JobAnalysisEntity>(entity =>
        {
            entity.HasKey(e => e.JobId);

            entity.Property(e => e.Seniority).HasConversion<string>();
            ConfigureJsonList<Requirement>(entity.Property(e => e.Requirements));
            ConfigureJsonList<string>(entity.Property(e => e.Keywords));
            ConfigureJsonList<string>(entity.Property(e => e.MatchedSkills));
            ConfigureJsonList<string>(entity.Property(e => e.MissingSkills));
        });

        modelBuilder.Entity<TailoringRunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Result).HasConversion(JsonColumn.Converter<TailoringResult>());
            entity.Metadata.FindProperty(nameof(TailoringRunEntity.Result))!.SetValueComparer(JsonColumn.Comparer<TailoringResult>());
        });

        modelBuilder.Entity<ModelCacheEntryEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<LearnedFieldMapEntity>(entity =>
        {
            entity.HasKey(e => new { e.Host, e.FormSignature });
            ConfigureJsonDictionary<string, string>(entity.Property(e => e.ElementToKey));
        });

        modelBuilder.Entity<ApplicationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.UpdatedAt);

            entity.Property(e => e.Status).HasConversion<string>();
        });
    }

    private static void ConfigureJsonList<TItem>(PropertyBuilder<List<TItem>> property)
    {
        property.HasConversion(JsonColumn.Converter<List<TItem>>());
        property.Metadata.SetValueComparer(JsonColumn.Comparer<List<TItem>>());
    }

    private static void ConfigureJsonDictionary<TKey, TValue>(PropertyBuilder<Dictionary<TKey, TValue>> property)
        where TKey : notnull
    {
        property.HasConversion(JsonColumn.Converter<Dictionary<TKey, TValue>>());
        property.Metadata.SetValueComparer(JsonColumn.Comparer<Dictionary<TKey, TValue>>());
    }
}
