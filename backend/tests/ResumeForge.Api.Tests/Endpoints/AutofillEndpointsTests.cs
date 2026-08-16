using System.Net;
using System.Net.Http.Json;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Application.Autofill;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/autofill</c> (CONTRACTS.md §9, §10).</summary>
[Collection("ResumeForgeApi")]
public sealed class AutofillEndpointsTests(ResumeForgeApiFactory factory)
{
    [Fact]
    public async Task Get_profile_returns_every_canonical_key_and_the_base_resume_document()
    {
        using var client = factory.CreateClient();

        // Guarantee a base resume exists so Documents is deterministic regardless of what
        // other tests in this shared run have already done.
        await client.PostAsync("/api/resumes/base", content: null);

        var profile = await client.GetFromJsonAsync<AutofillProfile>("/api/autofill/profile", TestJson.Options);

        profile.ShouldNotBeNull();
        profile.Fields["email"].ShouldBe("test.candidate@example.com");
        profile.Fields["firstName"].ShouldBe("Test");
        profile.Fields["lastName"].ShouldBe("Candidate");
        // Unknown canonical keys are omitted rather than present-with-null, matching
        // frontend/src/api/types.ts's Partial<Record<CanonicalFieldKey, ...>> shape.
        profile.Fields.ShouldNotContainKey("workAuthorization");

        var document = profile.Documents.ShouldHaveSingleItem();
        document.Kind.ShouldBe("resume");
        document.DownloadUrl.ShouldStartWith("http"); // absolute: consumed from a chrome-extension:// origin
        document.DownloadUrl.ShouldContain("?format=pdf");
    }

    [Fact]
    public async Task Resolve_with_no_unresolved_fields_returns_an_empty_result_without_calling_the_model()
    {
        using var client = factory.CreateClient();

        var request = new ResolveFieldsRequest { Host = "boards.greenhouse.io", FormSignature = "sig-empty", Fields = [] };

        using var response = await client.PostAsJsonAsync("/api/autofill/resolve", request, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResolveFieldsResponse>(TestJson.Options);
        body.ShouldNotBeNull();
        body.Resolutions.ShouldBeEmpty();
        body.Usage.ModelCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Resolve_with_an_unresolved_field_returns_501_under_the_heuristic_model()
    {
        // HeuristicLanguageModel (the model in effect whenever no API key is configured,
        // per CONTRACTS.md §8) only ever proposes tailoring commands; it has no support for
        // the "field-resolutions" schema this route needs. See the implementation report.
        using var client = factory.CreateClient();

        var request = new ResolveFieldsRequest
        {
            Host = "boards.greenhouse.io",
            FormSignature = "sig-nonempty",
            Fields = [new UnresolvedField { ElementId = "el-1", Label = "Email", InputType = "email" }],
        };

        using var response = await client.PostAsJsonAsync("/api/autofill/resolve", request, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Save_and_get_fieldmap_roundtrip()
    {
        using var client = factory.CreateClient();
        const string host = "boards.greenhouse.io";
        const string formSignature = "sig-roundtrip";

        var map = new LearnedFieldMap
        {
            Host = host,
            FormSignature = formSignature,
            ElementToKey = new Dictionary<string, string> { ["el-1"] = "email" },
            LearnedAt = DateTimeOffset.UtcNow,
            HitCount = 1,
        };

        using var saveResponse = await client.PostAsJsonAsync("/api/autofill/fieldmap", map, TestJson.Options);
        saveResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fetched = await client.GetFromJsonAsync<LearnedFieldMap>(
            $"/api/autofill/fieldmap/{host}?formSignature={formSignature}", TestJson.Options);

        fetched.ShouldNotBeNull();
        fetched.ElementToKey["el-1"].ShouldBe("email");
    }

    [Fact]
    public async Task Get_fieldmap_for_an_unknown_form_returns_200_with_a_null_body()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/autofill/fieldmap/unknown.example?formSignature=none");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Trim().ShouldBe("null");
    }

    [Fact]
    public async Task Get_fieldmap_without_form_signature_returns_400()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/autofill/fieldmap/unknown.example");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
