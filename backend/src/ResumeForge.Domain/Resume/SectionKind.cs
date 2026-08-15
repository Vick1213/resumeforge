namespace ResumeForge.Domain.Resume;

/// <summary>
/// The sections that can appear in a rendered resume, in the order controlled by
/// <see cref="ResumeDocument.SectionOrder"/>.
/// </summary>
public enum SectionKind
{
    /// <summary>The summary block.</summary>
    Summary,

    /// <summary>The skills section.</summary>
    Skills,

    /// <summary>The experience section.</summary>
    Experience,

    /// <summary>The projects section.</summary>
    Projects,

    /// <summary>The education section.</summary>
    Education,

    /// <summary>The certifications section.</summary>
    Certifications,
}
