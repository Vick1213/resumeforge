using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;

namespace ResumeForge.Application.Abstractions;

/// <summary>
/// Port to tailoring run persistence: the durable record behind
/// <c>POST /api/tailor</c> and <c>GET /api/tailor/{runId}/trace</c>.
/// </summary>
public interface ITailoringRunRepository
{
    /// <summary>Persists a completed run and returns its assigned run id.</summary>
    Task<string> SaveAsync(TailoringRunRecord run, CancellationToken ct);

    /// <summary>Retrieves a run by id, or null when it does not exist.</summary>
    Task<TailoringRunRecord?> GetAsync(string runId, CancellationToken ct);

    /// <summary>Retrieves just the graph trace for a run, for the live pipeline view.</summary>
    Task<IReadOnlyList<GraphNodeTrace>?> GetTraceAsync(string runId, CancellationToken ct);
}

/// <summary>A durable record of one tailoring run, as stored by <see cref="ITailoringRunRepository"/>.</summary>
public sealed record TailoringRunRecord
{
    /// <summary>The run's own id.</summary>
    public required string Id { get; init; }

    /// <summary>The job posting this run tailored against.</summary>
    public required string JobId { get; init; }

    /// <summary>The base resume this run started from, if not the default.</summary>
    public string? BaseResumeId { get; init; }

    /// <summary>The full result produced by <see cref="ITailoringService"/>.</summary>
    public required TailoringResult Result { get; init; }

    /// <summary>When this run was executed.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
