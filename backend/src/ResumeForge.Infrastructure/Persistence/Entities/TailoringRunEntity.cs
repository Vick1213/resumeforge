using ResumeForge.Application.Tailoring;

namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>TailoringRuns</c> table.</summary>
public sealed class TailoringRunEntity
{
    public required string Id { get; set; }

    public required string JobId { get; set; }

    public string? BaseResumeId { get; set; }

    /// <summary>The full run result, including its diff, coverage report, and graph trace.</summary>
    public required TailoringResult Result { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
