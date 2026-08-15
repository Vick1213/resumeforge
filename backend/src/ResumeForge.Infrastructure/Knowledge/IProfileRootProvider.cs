namespace ResumeForge.Infrastructure.Knowledge;

/// <summary>
/// Resolves the filesystem directory the markdown knowledge base is read from and written
/// to, once per process, and logs how it was resolved so a wrong working directory
/// produces a clear diagnostic instead of a silently empty knowledge base.
/// </summary>
public interface IProfileRootProvider
{
    /// <summary>The resolved, absolute path to the <c>profile/</c> directory.</summary>
    string ProfileRoot { get; }
}
