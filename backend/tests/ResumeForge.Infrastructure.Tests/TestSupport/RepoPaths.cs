namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>
/// Locates the repository root and its <c>profile/</c> directory relative to the test
/// assembly's own location, by walking up looking for <c>ResumeForge.slnx</c>. Never
/// hardcodes an absolute path, so this works on any machine (including CI) regardless of
/// where the repository is checked out.
/// </summary>
public static class RepoPaths
{
    private const string RepoMarkerFile = "ResumeForge.slnx";

    /// <summary>The repository root directory.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The repository's <c>profile/</c> directory.</summary>
    public static string ProfileDirectory { get; } = Path.Combine(RepositoryRoot, "profile");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoMarkerFile)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate '{RepoMarkerFile}' by walking up from '{AppContext.BaseDirectory}'.");
    }
}
