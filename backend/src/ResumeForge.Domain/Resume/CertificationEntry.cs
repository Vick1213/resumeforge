namespace ResumeForge.Domain.Resume;

/// <summary>
/// A single entry in the certifications section of a resume.
/// </summary>
public sealed record CertificationEntry
{
    /// <summary>The entry's id, e.g. <c>"cert:az-204"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Certification name.</summary>
    public required string Name { get; init; }

    /// <summary>Issuing organization.</summary>
    public string? Issuer { get; init; }

    /// <summary>Date the certification was issued.</summary>
    public DateOnly? IssuedOn { get; init; }

    /// <summary>URL where the credential can be verified.</summary>
    public string? CredentialUrl { get; init; }

    /// <summary>Whether this entry is included in the rendered resume.</summary>
    public bool Included { get; init; } = true;
}
