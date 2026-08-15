namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>JobPostings</c> table.</summary>
public sealed class JobPostingEntity
{
    public required string Id { get; set; }

    public required string SourceUrl { get; set; }

    public string? Company { get; set; }

    public string? Title { get; set; }

    public string? Location { get; set; }

    public required string RawText { get; set; }

    public required DateTimeOffset FetchedAt { get; set; }
}
