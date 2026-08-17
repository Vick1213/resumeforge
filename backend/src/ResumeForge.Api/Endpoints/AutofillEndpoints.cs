using ResumeForge.Api.Contracts;
using ResumeForge.Api.ExceptionHandling;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Autofill;
using ResumeForge.Application.Tailoring;
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

    private static async Task<IResult> ResolveAsync(
        ResolveFieldsRequest request, ILanguageModel languageModel, IKnowledgeBaseReader knowledgeBaseReader, CancellationToken ct)
    {
        if (request.Fields.Count == 0)
        {
            return TypedResults.Ok(new ResolveFieldsResponse { Resolutions = [], Usage = TokenUsage.Empty });
        }

        // Grounding material for free-text answers is only useful — and only worth reading
        // — once the effort actually unlocks that tier-3 behaviour (CONTRACTS.md §10).
        var profile = request.Effort >= ModelEffort.Thorough
            ? await knowledgeBaseReader.ReadAsync(ct).ConfigureAwait(false)
            : null;

        var modelRequest = new ModelRequest
        {
            System = BuildSystemPrompt(request.Effort),
            User = BuildBrief(request, profile),
            SchemaName = JsonSchemaRegistry.FieldResolutionsSchemaName,
            MaxOutputTokens = MaxOutputTokensFor(request.Effort),
            Temperature = 0,
            CacheKey = $"autofill:{request.Host}:{request.FormSignature}",
        };

        var response = await languageModel.CompleteAsync<IReadOnlyList<FieldResolution>>(modelRequest, ct).ConfigureAwait(false);

        // Deterministic backstop, independent of which ILanguageModel produced the
        // response: optionValue is never surfaced for a select/radio choice below
        // Standard effort, nor as a free-text answer for any other input type below
        // Thorough — regardless of what a real provider chose to return.
        var resolutions = EnforceEffortGate(request, response.Value);

        return TypedResults.Ok(new ResolveFieldsResponse { Resolutions = resolutions, Usage = response.Usage });
    }

    private static string BuildSystemPrompt(ModelEffort effort)
    {
        const string Base =
            "You resolve web form fields to canonical autofill keys. You only ever emit " +
            "one of the given canonical keys per field, or an empty string when a field " +
            "genuinely cannot be mapped.";

        if (effort < ModelEffort.Thorough)
        {
            return Base;
        }

        // Free-text answers are subject to the same fabrication rule as resume bullets
        // (CONTRACTS.md §10): an answer may only assert what the knowledge base supports.
        return Base +
            " For select/radio fields, propose the best-matching option in optionValue. " +
            "For text or textarea fields asking an open question (e.g. \"why this role\", " +
            "\"describe a project\"), you may also propose a free-text answer in " +
            "optionValue, grounded only in the PROFILE section of the brief — never invent " +
            "a fact, employer, date, or metric that isn't there. Leave optionValue null " +
            "rather than guess.";
    }

    /// <summary>
    /// Per CONTRACTS.md §10's effort table: a longer per-answer budget at
    /// <see cref="ModelEffort.Maximum"/>, a moderate one once free-text answers unlock at
    /// <see cref="ModelEffort.Thorough"/>, and the original fixed budget below that (where
    /// tier 3 only ever emits a canonical key and, at Standard, a chosen option — both cheap).
    /// </summary>
    private static int MaxOutputTokensFor(ModelEffort effort) => effort switch
    {
        // Full behaves as Maximum here: it is a tailoring tier (it unlocks rewriting every
        // bullet and every project tagline), and autofill emits none of those ops — so it
        // must not fall through to the 512 floor, which would give the most expensive setting
        // the smallest per-answer budget.
        ModelEffort.Maximum or ModelEffort.Full => 2048,
        ModelEffort.Thorough => 1024,
        _ => 512,
    };

    /// <summary>
    /// Strips <see cref="FieldResolution.OptionValue"/> from any resolution the requested
    /// effort does not license: a select/radio choice requires at least
    /// <see cref="ModelEffort.Standard"/>; a free-text answer for any other input type
    /// requires at least <see cref="ModelEffort.Thorough"/>. Enforced here, deterministically,
    /// rather than trusted from the model's own response.
    /// </summary>
    private static IReadOnlyList<FieldResolution> EnforceEffortGate(ResolveFieldsRequest request, IReadOnlyList<FieldResolution> resolutions)
    {
        var inputTypeByElement = request.Fields.ToDictionary(f => f.ElementId, f => f.InputType, StringComparer.Ordinal);

        return [.. resolutions.Select(resolution =>
        {
            if (resolution.OptionValue is null || !inputTypeByElement.TryGetValue(resolution.ElementId, out var inputType))
            {
                return resolution;
            }

            var isChoiceInput = string.Equals(inputType, "select", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(inputType, "radio", StringComparison.OrdinalIgnoreCase);

            var allowed = isChoiceInput ? request.Effort >= ModelEffort.Standard : request.Effort >= ModelEffort.Thorough;

            return allowed ? resolution : resolution with { OptionValue = null };
        })];
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

    private static string BuildBrief(ResolveFieldsRequest request, KnowledgeBaseSnapshot? profile)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("EFFORT|").Append(EffortToken(request.Effort)).Append('\n');
        sb.Append("CANONICAL-KEYS: ").Append(string.Join(',', CanonicalFieldKeys)).Append('\n');
        sb.Append("HOST: ").Append(request.Host).Append('\n');

        if (profile is not null)
        {
            AppendProfileSection(sb, profile);
        }

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

    /// <summary>
    /// Compact, KB-grounded material for free-text answers: the default summary plus a
    /// handful of the most substantial bullets across every knowledge-base item, each
    /// truncated. Gives a real model concrete evidence to draw an honest "describe a
    /// project"-style answer from without shipping the whole knowledge base — the same
    /// token-economics discipline <c>BriefBuilder</c> applies to the tailoring brief.
    /// </summary>
    private static void AppendProfileSection(System.Text.StringBuilder sb, KnowledgeBaseSnapshot profile)
    {
        sb.Append("PROFILE\n");

        if (!string.IsNullOrWhiteSpace(profile.DefaultSummary))
        {
            sb.Append("summary|").Append(TruncateForBrief(profile.DefaultSummary, 400)).Append('\n');
        }

        foreach (var bullet in profile.Items.SelectMany(i => i.Bullets.Select(b => b.Text)).Where(t => t.Length > 0).Take(10))
        {
            sb.Append("evidence|").Append(TruncateForBrief(bullet, 200)).Append('\n');
        }
    }

    private static string TruncateForBrief(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, Math.Max(0, max - 1)), "…");

    private static string EffortToken(ModelEffort effort) => effort switch
    {
        ModelEffort.Minimal => "minimal",
        ModelEffort.Standard => "standard",
        ModelEffort.Thorough => "thorough",
        ModelEffort.Maximum => "maximum",
        ModelEffort.Full => "full",
        _ => "standard",
    };

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
