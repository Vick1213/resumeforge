using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Autofill;
using ResumeForge.Infrastructure.Ai;

namespace ResumeForge.Api.Endpoints;

/// <summary>Maps the <c>/api/autofill</c> routes (CONTRACTS.md §9, §10).</summary>
public static class AutofillEndpoints
{
    /// <summary>
    /// The closed set of canonical autofill field keys the extension and backend agree on
    /// (CONTRACTS.md §10).
    /// </summary>
    private static readonly IReadOnlyList<string> CanonicalFieldKeys =
    [
        "firstName", "lastName", "fullName", "preferredName", "email", "phone",
        "addressLine1", "addressLine2", "city", "state", "postalCode", "country",
        "linkedin", "github", "portfolio", "website",
        "currentCompany", "currentTitle", "yearsExperience",
        "workAuthorization", "requiresSponsorship", "willingToRelocate",
        "noticePeriod", "desiredSalary", "availableStartDate",
        "gender", "ethnicity", "veteranStatus", "disabilityStatus",
        "howDidYouHear", "referredBy",
    ];

    /// <summary>Registers the autofill routes on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapAutofillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/autofill").WithTags("Autofill");

        group.MapGet("/profile", GetProfileAsync)
            .WithName("GetAutofillProfile")
            .Produces<AutofillProfile>();

        group.MapPost("/resolve", ResolveAsync)
            .WithName("ResolveAutofillFields")
            .Produces<ResolveFieldsResponse>();

        group.MapPost("/fieldmap", SaveFieldMapAsync)
            .WithName("SaveLearnedFieldMap")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/fieldmap/{host}", GetFieldMapAsync)
            .WithName("GetLearnedFieldMap")
            .Produces<LearnedFieldMap?>();

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        HttpRequest httpRequest, IKnowledgeBaseReader knowledgeBaseReader, IResumeRepository resumeRepository, CancellationToken ct)
    {
        var snapshot = await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false);
        var basics = snapshot.Basics;

        // frontend/src/api/types.ts models Fields as Partial<Record<CanonicalFieldKey, ...>>
        // — an unknown field is an absent key, not a key mapped to null — so only fields
        // with a real value are added.
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Set(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[key] = value;
            }
        }

        var (firstName, lastName) = SplitName(basics.FullName);
        Set("firstName", firstName);
        Set("lastName", lastName);
        Set("fullName", basics.FullName);
        Set("email", basics.Email);
        Set("phone", basics.Phone);
        Set("linkedin", basics.LinkedIn);
        Set("github", basics.GitHub);
        Set("website", basics.Website);
        Set("portfolio", basics.Website);
        Set("currentTitle", basics.Headline);

        // basics.Location is free-form ("Seattle, WA") and there is nowhere else in the
        // knowledge base holding a structured address, work-authorization, compensation,
        // or demographic answer, so every other canonical key is left absent. See the
        // implementation report for this gap.

        var documents = new List<AutofillDocument>();
        var baseResume = await resumeRepository.GetBaseAsync(ct).ConfigureAwait(false);
        if (baseResume is not null)
        {
            // Absolute, including scheme and authority: the consumer is the extension's
            // background service worker (origin chrome-extension://<id>), and a relative
            // URL would resolve against that origin instead of this API. Built from the
            // incoming request rather than a hardcoded host so it stays correct behind a
            // proxy or a different port. POST-only per CONTRACTS.md §9 (there is no GET
            // download route); RenderEndpoints also accepts 'format' as a query parameter
            // (in addition to the request body) so this URL is actually usable as shown.
            var origin = $"{httpRequest.Scheme}://{httpRequest.Host}";
            documents.Add(new AutofillDocument
            {
                Kind = "resume",
                FileName = "resume.pdf",
                DownloadUrl = $"{origin}/api/render/{baseResume.Id}?format=pdf",
            });
        }

        return TypedResults.Ok(new AutofillProfile { Fields = fields, Documents = documents });
    }

    private static async Task<IResult> ResolveAsync(ResolveFieldsRequest request, ILanguageModel languageModel, CancellationToken ct)
    {
        if (request.Fields.Count == 0)
        {
            return TypedResults.Ok(new ResolveFieldsResponse { Resolutions = [], Usage = TokenUsage.Empty });
        }

        var modelRequest = new ModelRequest
        {
            System =
                "You resolve web form fields to canonical autofill keys. You only ever emit " +
                "one of the given canonical keys per field, or an empty string when a field " +
                "genuinely cannot be mapped.",
            User = BuildBrief(request),
            SchemaName = JsonSchemaRegistry.FieldResolutionsSchemaName,
            MaxOutputTokens = 512,
            Temperature = 0,
            CacheKey = $"autofill:{request.Host}:{request.FormSignature}",
        };

        var response = await languageModel.CompleteAsync<IReadOnlyList<FieldResolution>>(modelRequest, ct).ConfigureAwait(false);

        return TypedResults.Ok(new ResolveFieldsResponse { Resolutions = response.Value, Usage = response.Usage });
    }

    private static async Task<IResult> SaveFieldMapAsync(LearnedFieldMap map, ILearnedFieldMapRepository repository, CancellationToken ct)
    {
        await repository.SaveAsync(map, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetFieldMapAsync(
        string host, string? formSignature, ILearnedFieldMapRepository repository, CancellationToken ct)
    {
        // ILearnedFieldMapRepository.GetAsync requires (host, formSignature) — the table's
        // own primary key — but CONTRACTS.md's route only names {host}. formSignature is
        // added as a required query parameter so the port can actually be called; see the
        // implementation report for this gap.
        if (string.IsNullOrWhiteSpace(formSignature))
        {
            return ProblemResults.BadRequest("Query parameter 'formSignature' is required.");
        }

        var map = await repository.GetAsync(host, formSignature, ct).ConfigureAwait(false);

        // The contracted response type is nullable (LearnedFieldMap?), so a miss is a 200
        // with a literal JSON null body rather than a 404. Both TypedResults.Ok<T> and
        // TypedResults.Json<T> write an empty body (Content-Length: 0) for a null value
        // rather than the JSON literal "null" — verified empirically, not merely assumed —
        // which is not valid JSON a client can safely call response.json() on. Text(...)
        // bypasses that value-based short-circuit entirely.
        return map is null
            ? TypedResults.Text("null", "application/json")
            : TypedResults.Json(map, statusCode: StatusCodes.Status200OK);
    }

    private static string BuildBrief(ResolveFieldsRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("CANONICAL-KEYS: ").Append(string.Join(',', CanonicalFieldKeys)).Append('\n');
        sb.Append("HOST: ").Append(request.Host).Append('\n');
        sb.Append("FIELDS\n");

        foreach (var field in request.Fields)
        {
            sb.Append(field.ElementId).Append('|')
                .Append(field.InputType).Append('|')
                .Append(field.Label).Append('|')
                .Append(field.Name).Append('|')
                .Append(field.Placeholder).Append('|')
                .Append(field.AutoComplete).Append('|')
                .Append(string.Join(';', field.Options))
                .Append('\n');
        }

        return sb.ToString();
    }

    private static (string? FirstName, string? LastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (null, null);
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[^1]),
        };
    }
}
