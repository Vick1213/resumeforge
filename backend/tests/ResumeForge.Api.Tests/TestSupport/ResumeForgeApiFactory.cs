using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResumeForge.Infrastructure.Ai;
using ResumeForge.Infrastructure.GitHub;

namespace ResumeForge.Api.Tests.TestSupport;

/// <summary>
/// A <see cref="WebApplicationFactory{Program}"/> that points the running app at an
/// isolated temp knowledge-base directory and an isolated temp SQLite database file, so
/// integration tests never touch a developer's real <c>profile/</c> data or database.
/// Also clears every provider API key in
/// <see cref="AiProviderCatalog.KeyEnvironmentVariables"/> for the lifetime of the factory,
/// and pins <c>ResumeForge:Ai:Provider</c> to <c>heuristic</c>, so <c>ILanguageModel</c>
/// always resolves to the no-network heuristic implementation regardless of what the host
/// machine's environment happens to have set — keeping these tests deterministic and
/// network-free (see CONTRACTS.md §8). Clearing one named key was not enough: a developer
/// with <c>DEEPSEEK_API_KEY</c> exported (as <c>scripts/dev.sh</c> arranges) had <c>auto</c>
/// resolve to a real hosted provider, which failed four tests on their machine and none in
/// CI.
/// </summary>
public sealed class ResumeForgeApiFactory : WebApplicationFactory<Program>
{
    private readonly string _profileRoot;
    private readonly string _dbPath;
    private readonly Dictionary<string, string?> _previousApiKeys = [];

    /// <summary>Creates the factory, writing the fixture profile and clearing the API key.</summary>
    public ResumeForgeApiFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), "resumeforge-api-tests", Guid.NewGuid().ToString("N"));
        _profileRoot = Path.Combine(root, "profile");
        _dbPath = Path.Combine(root, "resumeforge-test.db");

        Directory.CreateDirectory(root);
        TestProfileFixture.Write(_profileRoot);

        foreach (var variable in AiProviderCatalog.KeyEnvironmentVariables)
        {
            _previousApiKeys[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>The single stub repository <see cref="StubGitHubRepoJson"/> returns for the GitHub API, by name.</summary>
    public const string StubGitHubRepoName = "graph-runner-clone";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ResumeForge"] = $"Data Source={_dbPath}",
                ["ResumeForge:ProfileRoot"] = _profileRoot,
                ["ResumeForge:Ai:Provider"] = AiProviderCatalog.Heuristic.Name,
            });
        });

        // GitHubProjectImporter always hits the real GitHub REST API through its named
        // HttpClient; stubbing that client here keeps every test in this run network-free,
        // rather than only the ones that happen to pass an empty repository list.
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(GitHubProjectImporter.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(RespondToGitHubRequest));
        });
    }

    private static HttpResponseMessage RespondToGitHubRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.StartsWith("/users/", StringComparison.Ordinal) && path.EndsWith("/repos", StringComparison.Ordinal))
        {
            return JsonResponse(HttpStatusCode.OK, $"[{StubGitHubRepoJson}]");
        }

        if (path.EndsWith($"/{StubGitHubRepoName}", StringComparison.Ordinal))
        {
            return JsonResponse(HttpStatusCode.OK, StubGitHubRepoJson);
        }

        return JsonResponse(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
    }

    private const string StubGitHubRepoJson = $$"""
        {
          "name": "{{StubGitHubRepoName}}",
          "description": "A stub repository used only by API integration tests.",
          "html_url": "https://github.com/test-candidate/graph-runner-clone",
          "homepage": null,
          "language": "TypeScript",
          "topics": ["testing"],
          "stargazers_count": 3,
          "created_at": "2023-01-01T00:00:00Z",
          "pushed_at": "2023-06-01T00:00:00Z",
          "fork": false
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        foreach (var (variable, value) in _previousApiKeys)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }

        TryDeleteDirectory(Path.GetDirectoryName(_profileRoot));
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only; a locked file (e.g. the SQLite database) on some
            // platforms should never fail the test run.
        }
    }
}
