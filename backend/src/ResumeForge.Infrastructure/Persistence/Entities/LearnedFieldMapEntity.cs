using ResumeForge.Application.Tailoring;

namespace ResumeForge.Infrastructure.Persistence.Entities;

/// <summary>EF entity backing the <c>LearnedFieldMaps</c> table; keyed by <c>(Host, FormSignature)</c>.</summary>
public sealed class LearnedFieldMapEntity
{
    public required string Host { get; set; }

    public required string FormSignature { get; set; }

    public required Dictionary<string, string> ElementToKey { get; set; }

    public required DateTimeOffset LearnedAt { get; set; }

    public required ModelEffort LearnedAtEffort { get; set; }

    public required int HitCount { get; set; }
}
