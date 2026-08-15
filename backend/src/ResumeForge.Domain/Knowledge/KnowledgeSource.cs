namespace ResumeForge.Domain.Knowledge;

/// <summary>
/// Where a knowledge item's content originated.
/// </summary>
public enum KnowledgeSource
{
    /// <summary>Hand-written by the user.</summary>
    Manual,

    /// <summary>Generated from a GitHub import and safe to regenerate.</summary>
    GitHub,
}
