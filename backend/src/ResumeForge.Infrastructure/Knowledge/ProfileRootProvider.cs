using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ResumeForge.Infrastructure.Knowledge;

/// <summary>
/// Default <see cref="IProfileRootProvider"/>. Honors <c>ResumeForge:ProfileRoot</c> when
/// configured; otherwise walks up from <see cref="AppContext.BaseDirectory"/> looking for
/// the repository root (identified by <c>ResumeForge.slnx</c> next to a <c>profile</c>
/// directory) so <c>dotnet run</c> from anywhere under the repository finds the real
/// sample data. The resolution and its outcome are always logged at startup.
/// </summary>
public sealed class ProfileRootProvider : IProfileRootProvider
{
    private const string ConfigKey = "ResumeForge:ProfileRoot";
    private const string RepoMarkerFile = "ResumeForge.slnx";
    private const string ProfileDirectoryName = "profile";

    /// <summary>Resolves and logs the profile root immediately.</summary>
    public ProfileRootProvider(IConfiguration configuration, ILogger<ProfileRootProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        ProfileRoot = Resolve(configuration, logger);
    }

    /// <inheritdoc />
    public string ProfileRoot { get; }

    private static string Resolve(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration[ConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolvedConfigured = Path.GetFullPath(configured, AppContext.BaseDirectory);
            logger.LogInformation(
                "Profile root resolved to '{ProfileRoot}' from configuration key '{ConfigKey}'.",
                resolvedConfigured, ConfigKey);
            return resolvedConfigured;
        }

        var discovered = FindRepositoryProfileDirectory(AppContext.BaseDirectory);
        if (discovered is not null)
        {
            logger.LogInformation(
                "Profile root resolved to '{ProfileRoot}' by locating the repository root above '{BaseDirectory}'. " +
                "Set '{ConfigKey}' to override.",
                discovered, AppContext.BaseDirectory, ConfigKey);
            return discovered;
        }

        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ProfileDirectoryName));
        var fallbackExists = Directory.Exists(fallback);

        if (fallbackExists)
        {
            logger.LogInformation(
                "Profile root resolved to '{ProfileRoot}' (found next to the running assembly).",
                fallback);
        }
        else
        {
            logger.LogWarning(
                "Could not locate a '{Marker}' repository root above '{BaseDirectory}', and no '{ProfileDir}' " +
                "directory exists next to the running assembly. Falling back to '{Fallback}', which does not " +
                "exist yet — the knowledge base will read as empty until '{ConfigKey}' is set explicitly or the " +
                "process is run from within the repository.",
                RepoMarkerFile, AppContext.BaseDirectory, ProfileDirectoryName, fallback, ConfigKey);
        }

        return fallback;
    }

    private static string? FindRepositoryProfileDirectory(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir is not null)
        {
            var candidateProfile = Path.Combine(dir.FullName, ProfileDirectoryName);
            var candidateMarker = Path.Combine(dir.FullName, RepoMarkerFile);

            if (Directory.Exists(candidateProfile) && File.Exists(candidateMarker))
            {
                return candidateProfile;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
