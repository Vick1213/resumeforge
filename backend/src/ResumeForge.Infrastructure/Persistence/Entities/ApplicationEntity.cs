using ResumeForge.Application.Abstractions;

namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>Applications</c> table.</summary>
public sealed class ApplicationEntity
{
    public required string Id { get; set; }

    public required string JobId { get; set; }

    public string? ResumeId { get; set; }

    public required ApplicationStatus Status { get; set; }

    public string? Notes { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
