namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// The category of a knowledge-base markdown file, mirroring the profile directory it
/// lives under (or the single <c>basics.md</c> file).
/// </summary>
public enum KnowledgeItemType
{
    /// <summary>A file under <c>profile/experience/</c>.</summary>
    Experience,

    /// <summary>A file under <c>profile/projects/</c>.</summary>
    Project,

    /// <summary>A file under <c>profile/education/</c>.</summary>
    Education,

    /// <summary>A file under <c>profile/certifications/</c>.</summary>
    Certification,

    /// <summary>The single <c>profile/basics.md</c> file.</summary>
    Basics,
}
