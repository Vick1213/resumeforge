using System.Text;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Formatting;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Infrastructure.GitHub;
using ResumeForge.Infrastructure.Knowledge;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/knowledge</c> routes (CONTRACTS.md §9).</summary>
public static class KnowledgeEndpoints
{
    /// <summary>Registers the knowledge-base routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/knowledge").WithTags("Knowledge");

        group.MapGet("/", ListAsync)
            .WithName("ListKnowledge")
            .Produces<IReadOnlyList<KnowledgeItemDto>>();

        group.MapGet("/{id}", GetAsync)
            .WithName("GetKnowledgeItem")
            .Produces<KnowledgeItemDetailDto>();

        group.MapPut("/{id}", UpsertAsync)
            .WithName("UpsertKnowledgeItem")
            .Produces<KnowledgeItemDetailDto>();

        group.MapDelete("/{id}", DeleteAsync)
            .WithName("DeleteKnowledgeItem")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/import/github", ImportGitHubAsync)
            .WithName("ImportGitHubProjects")
            .Produces<Contracts.GitHubImportResult>();

        return app;
    }

    private static async Task<IResult> ListAsync(IKnowledgeBaseReader reader, IProfileRootProvider profileRoot, CancellationToken ct)
    {
        var snapshot = await reader.ReadAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KnowledgeItemDto>>([.. snapshot.Items.Select(i => ToDto(i, profileRoot))]);
    }

    private static async Task<IResult> GetAsync(string id, IKnowledgeBaseReader reader, IProfileRootProvider profileRoot, CancellationToken ct)
    {
        var snapshot = await reader.ReadAsync(ct).ConfigureAwait(false);
        var item = snapshot.Items.FirstOrDefault(i => i.Id.ToString() == id);

        return item is null
            ? ProblemResults.NotFound($"No knowledge item with id '{id}' was found.")
            : TypedResults.Ok(ToDetailDto(item, snapshot, profileRoot));
    }

    private static async Task<IResult> UpsertAsync(
        string id,
        UpsertKnowledgeRequest request,
        IKnowledgeBaseReader reader,
        IProfileRootProvider profileRoot,
        CancellationToken ct)
    {
        if (!EntityId.TryParse(id, out var entityId) || !TryMapType(entityId.Kind, out var type))
        {
            return ProblemResults.BadRequest($"'{id}' is not a valid knowledge item id (expected an 'exp:', 'prj:', 'edu:', or 'cert:' id).");
        }

        // The frontend's UpsertKnowledgeRequest is just { rawMarkdown }, but
        // IKnowledgeBaseWriter.WriteAsync only ever reconstructs a file from a KnowledgeItem's
        // structured fields — it has no "write this exact text" method, and the reader's
        // frontmatter parser is internal to Infrastructure, so there is no supported way to
        // parse-then-round-trip through it either. Writing the file directly here (via the
        // same public IProfileRootProvider the real writer uses) is the closest fit available
        // without touching Infrastructure; see the implementation report for this gap.
        var directory = Path.Combine(profileRoot.ProfileRoot, DirectoryName(type));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, entityId.Slug + ".md");
        await File.WriteAllTextAsync(path, request.RawMarkdown, ct).ConfigureAwait(false);

        var snapshot = await reader.ReadAsync(ct).ConfigureAwait(false);
        var written = snapshot.Items.FirstOrDefault(i => i.Id == entityId);

        if (written is not null)
        {
            return TypedResults.Ok(ToDetailDto(written, snapshot, profileRoot));
        }

        var relativePath = $"{DirectoryName(type)}/{entityId.Slug}.md";
        var fileDiagnostics = snapshot.Diagnostics.Where(d => d.FilePath == relativePath).ToList();

        return fileDiagnostics.Count > 0
            ? ProblemResults.BadRequest(string.Join(' ', fileDiagnostics.Select(d => d.Message)))
            : ProblemResults.BadRequest($"Wrote '{id}' but could not read it back from the knowledge base.");
    }

    private static async Task<IResult> DeleteAsync(string id, IKnowledgeBaseWriter writer, CancellationToken ct)
    {
        if (!EntityId.TryParse(id, out var entityId) || !TryMapType(entityId.Kind, out _))
        {
            return ProblemResults.BadRequest($"'{id}' is not a valid knowledge item id (expected an 'exp:', 'prj:', 'edu:', or 'cert:' id).");
        }

        // IKnowledgeBaseWriter.DeleteAsync is a no-op when the file is already absent, so
        // this is idempotent and always succeeds once the id itself is well-formed.
        await writer.DeleteAsync(entityId, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ImportGitHubAsync(GitHubImportRequest request, IGitHubProjectImporter importer, CancellationToken ct)
    {
        var owned = await importer.ListRepositoriesAsync(request.Username, ct).ConfigureAwait(false);

        var selected = request.RepositoryNames.Count == 0
            ? owned
            : [.. owned.Where(r => request.RepositoryNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))];

        var previews = selected.Select(ToPreview).ToList();

        IReadOnlyList<string> committed = [];
        if (request.Commit && selected.Count > 0)
        {
            var result = await importer.ImportAsync(request.Username, [.. selected.Select(r => r.Name)], ct).ConfigureAwait(false);
            committed = result.Imported;
        }

        return TypedResults.Ok(new Contracts.GitHubImportResult { Repos = previews, Committed = committed });
    }

    private static bool TryMapType(EntityKind kind, out KnowledgeItemType type)
    {
        switch (kind)
        {
            case EntityKind.Experience:
                type = KnowledgeItemType.Experience;
                return true;
            case EntityKind.Project:
                type = KnowledgeItemType.Project;
                return true;
            case EntityKind.Education:
                type = KnowledgeItemType.Education;
                return true;
            case EntityKind.Certification:
                type = KnowledgeItemType.Certification;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static string DirectoryName(KnowledgeItemType type) => type switch
    {
        KnowledgeItemType.Experience => "experience",
        KnowledgeItemType.Project => "projects",
        KnowledgeItemType.Education => "education",
        KnowledgeItemType.Certification => "certifications",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported knowledge item type."),
    };

    private static KnowledgeItemDto ToDto(KnowledgeItem item, IProfileRootProvider profileRoot) => new()
    {
        Id = item.Id.ToString(),
        Kind = item.Type,
        Title = item.Title,
        Subtitle = BuildSubtitle(item),
        Tech = item.Tech,
        Tags = item.Tags,
        Source = item.Source,
        UpdatedAt = ReadLastWriteTime(item, profileRoot),
    };

    private static KnowledgeItemDetailDto ToDetailDto(KnowledgeItem item, KnowledgeBaseSnapshot snapshot, IProfileRootProvider profileRoot) => new()
    {
        Id = item.Id.ToString(),
        Kind = item.Type,
        Title = item.Title,
        Subtitle = BuildSubtitle(item),
        Tech = item.Tech,
        Tags = item.Tags,
        Source = item.Source,
        UpdatedAt = ReadLastWriteTime(item, profileRoot),
        RawMarkdown = item.RawMarkdown,
        Diagnostics = [.. snapshot.Diagnostics.Where(d => d.FilePath == item.FilePath)],
    };

    private static string? BuildSubtitle(KnowledgeItem item) => item.Type switch
    {
        KnowledgeItemType.Experience => item.Organization is { Length: > 0 } org
            ? $"{org} · {FormatDateRange(item)}"
            : FormatDateRange(item),
        KnowledgeItemType.Project => item.Extra.GetValueOrDefault("tagline"),
        KnowledgeItemType.Education => item.Organization,
        KnowledgeItemType.Certification => item.Organization,
        _ => null,
    };

    private static string? FormatDateRange(KnowledgeItem item) =>
        item.StartDate is { } start ? DateRangeFormatter.Format(start, item.IsCurrent ? null : item.EndDate) : null;

    private static DateTimeOffset ReadLastWriteTime(KnowledgeItem item, IProfileRootProvider profileRoot)
    {
        var path = Path.Combine(profileRoot.ProfileRoot, item.FilePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTimeOffset.UtcNow;
    }

    private static GitHubRepoPreview ToPreview(GitHubRepositorySummary repo) => new()
    {
        RepoName = repo.Name,
        Description = repo.Description,
        Language = repo.Language,
        Stars = repo.Stars,
        Url = repo.HtmlUrl,
        GeneratedMarkdown = BuildPreviewMarkdown(repo),
    };

    /// <summary>
    /// Approximates GitHubProjectImporter.ToKnowledgeItem's own generation strategy
    /// (description then topics as bullets), since that logic — and the markdown writer
    /// that would actually render it — is private to Infrastructure and there is no port to
    /// request a dry-run render from it. See the implementation report for this gap.
    /// </summary>
    private static string BuildPreviewMarkdown(GitHubRepositorySummary repo)
    {
        var tech = new List<string>();
        if (!string.IsNullOrWhiteSpace(repo.Language))
        {
            tech.Add(repo.Language);
        }

        foreach (var topic in repo.Topics)
        {
            if (!tech.Contains(topic, StringComparer.OrdinalIgnoreCase))
            {
                tech.Add(topic);
            }
        }

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("type: project\n");
        sb.Append($"name: {repo.Name}\n");
        if (!string.IsNullOrWhiteSpace(repo.Description))
        {
            sb.Append($"tagline: {repo.Description}\n");
        }

        sb.Append($"repoUrl: {repo.HtmlUrl}\n");
        if (!string.IsNullOrWhiteSpace(repo.Homepage))
        {
            sb.Append($"url: {repo.Homepage}\n");
        }

        sb.Append($"startDate: {repo.CreatedAt:yyyy-MM}\n");
        sb.Append($"endDate: {repo.PushedAt:yyyy-MM}\n");
        if (tech.Count > 0)
        {
            sb.Append($"tech: [{string.Join(", ", tech)}]\n");
        }

        sb.Append("source: github\n");
        sb.Append($"stars: {repo.Stars}\n");
        sb.Append("---\n\n");

        if (!string.IsNullOrWhiteSpace(repo.Description))
        {
            sb.Append($"- {repo.Description}\n");
        }

        if (repo.Topics.Count > 0)
        {
            sb.Append($"- Topics: {string.Join(", ", repo.Topics)}\n");
        }

        return sb.ToString();
    }
}
