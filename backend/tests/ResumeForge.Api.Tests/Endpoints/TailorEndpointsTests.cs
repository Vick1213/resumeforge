using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ResumeForge.Api.Contracts;
using ResumeForge.Api.Tests.TestSupport;
using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;
using Shouldly;
using Xunit;

namespace ResumeForge.Api.Tests.Endpoints;

/// <summary>Integration tests for <c>/api/tailor</c> (CONTRACTS.md §9).</summary>
[Collection("ResumeForgeApi")]
public sealed class TailorEndpointsTests(ResumeForgeApiFactory factory)
{
    private const string SamplePosting =
        "We are hiring a Senior Backend Engineer. Requirements: 5+ years of experience with " +
        "C# and .NET, strong PostgreSQL skills. Nice to have: Kubernetes experience.";

    [Fact]
    public async Task Dry_run_returns_a_result_without_persisting_a_new_resume()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);
        var resumeCountBefore = (await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options))!.Count;

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();
        result.Document.ShouldNotBeNull();
        result.Commands.ShouldNotBeNull();
        result.Coverage.ShouldNotBeNull();
        result.Trace.ShouldNotBeEmpty();

        var resumeCountAfter = (await client.GetFromJsonAsync<List<ResumeSummaryDto>>("/api/resumes", TestJson.Options))!.Count;
        resumeCountAfter.ShouldBe(resumeCountBefore);
    }

    [Fact]
    public async Task Non_dry_run_persists_a_new_resume_and_a_readable_trace()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var baseResponse = await client.PostAsync("/api/resumes/base", content: null);
        var baseResume = await baseResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options);
        baseResume.ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = false }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldNotBeNull();
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith("/api/tailor/");
        location.ShouldEndWith("/trace");

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        // CommandExecutor (Application layer) preserves the source document's Id/Name, so
        // this endpoint re-ids and renames the persisted variant rather than overwriting
        // the base resume it started from — see the implementation report.
        result.Document.Id.ShouldNotBe(baseResume.Id);

        var runId = location["/api/tailor/".Length..^"/trace".Length];

        using var traceResponse = await client.GetAsync($"/api/tailor/{runId}/trace");
        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trace = await traceResponse.Content.ReadFromJsonAsync<List<GraphNodeTrace>>(TestJson.Options);
        trace.ShouldNotBeNull();
        trace.ShouldNotBeEmpty();

        var persisted = await client.GetFromJsonAsync<ResumeDocument>($"/api/resumes/{result.Document.Id}", TestJson.Options);
        persisted.ShouldNotBeNull();
        persisted.Id.ShouldBe(result.Document.Id);
    }

    [Fact]
    public async Task Omitted_effort_and_explicit_standard_effort_produce_byte_identical_document_and_diff()
    {
        // The single most important invariant of the effort feature (CONTRACTS.md §6):
        // effort is purely additive, so a request that never mentions "effort" at all — the
        // exact shape every pre-effort client still sends — must behave identically to one
        // that names Standard explicitly. Both requests leave BaseResumeId null (current
        // base resume) and DryRun keeps each run side-effect-free so calling it twice is
        // safe; ResumeBuilder assigns a fresh id to each un-persisted build regardless of
        // effort, so Document.Id is excluded from the comparison below alongside the
        // timestamps, same as Id/CreatedAt/UpdatedAt would be for any two independent builds.
        using var client = factory.CreateClient();
        var jobId = await CreateJobAsync(client);

        using var omittedResponse = await client.PostAsJsonAsync(
            "/api/tailor",
            new { jobId, dryRun = true },
            TestJson.Options);
        omittedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var omitted = await omittedResponse.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);

        using var standardResponse = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, Effort = ModelEffort.Standard, DryRun = true },
            TestJson.Options);
        standardResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var standard = await standardResponse.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);

        omitted.ShouldNotBeNull();
        standard.ShouldNotBeNull();

        // Compared via a serialized projection rather than record equality: nested
        // IReadOnlyList<T> properties (Commands.Accepted, Coverage.Requirements, ...) don't
        // get structural equality from the generated record Equals, since two independently
        // deserialized responses never share list instances. Trace/Usage are deliberately
        // excluded — Trace carries real per-run elapsed time and both are unaffected by
        // effort at Standard, so they're not part of this guarantee.
        Project(omitted!).ShouldBe(Project(standard!));
    }

    // Id/CreatedAt/UpdatedAt come from a fresh ResumeBuilder.Build/TimeProvider.System call
    // for each independent run and are not part of the effort guarantee — two calls a few
    // milliseconds apart, or with no persisted base resume to share an id from, may
    // legitimately differ there even though every field effort could possibly influence is
    // identical.
    private static string Project(TailoringResult result) => System.Text.Json.JsonSerializer.Serialize(
        new
        {
            Document = result.Document with { Id = string.Empty, CreatedAt = default, UpdatedAt = default },
            result.Diff,
            result.Commands,
            result.Coverage,
        },
        TestJson.Options);

    [Fact]
    public async Task A_malformed_proposed_command_is_rejected_instead_of_failing_the_whole_run()
    {
        // Reproduces the live failure this fix responds to: a real provider proposed a run
        // of otherwise-good commands plus one setSectionOrder whose order named something
        // that isn't a legal SectionKind. That used to throw out of JsonSerializer.Deserialize
        // and take the entire run down as an HTTP 500 with nothing at all returned — even
        // though every other command was perfectly good. It must now come back as HTTP 200
        // with the good command applied and the bad one reported as a "malformed-command"
        // rejection (CONTRACTS.md §6: rejected, never silently dropped, and never allowed to
        // sink commands around it).
        var parseResults = new TailorCommandParseResultList(
        [
            new TailorCommandParseResult.Parsed(new SetSummaryCommand { Text = "A concise, tailored summary." }),
            new TailorCommandParseResult.Malformed(
                1,
                """{"op":"setSectionOrder","order":["engineering","skills","experience","projects","education","certifications"]}""",
                "The JSON value could not be converted to ResumeForge.Domain.Resume.SectionKind. Path: $.order[0]."),
        ]);

        var languageModel = Substitute.For<ILanguageModel>();
        languageModel
            .CompleteAsync<TailorCommandParseResultList>(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ModelResponse<TailorCommandParseResultList>
            {
                Value = parseResults,
                Usage = TokenUsage.Empty,
                FromCache = false,
            }));

        using var scopedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<ILanguageModel>(_ => languageModel)));
        using var client = scopedFactory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.Commands.Accepted.ShouldContain(c => c is SetSummaryCommand);

        var rejection = result.Commands.Rejected.ShouldHaveSingleItem();
        rejection.Code.ShouldBe("malformed-command");
        rejection.Command.ShouldBeNull();
        rejection.Reason.ShouldContain("SectionKind");

        result.Document.Summary.ShouldBe("A concise, tailored summary.");
    }

    [Fact]
    public async Task Tailor_for_unknown_job_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = "does-not-exist" }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_trace_for_unknown_run_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/tailor/does-not-exist/trace");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_jd_matched_skills_are_emphasized_in_the_tailored_resume()
    {
        // Regression for "emphasizeSkills bolds every skill": the fixture profile carries
        // five skills (csharp, dotnet, postgresql, typescript, nodejs) across its one
        // experience entry and one project. A JD that only asks for C# and PostgreSQL
        // must emphasize exactly those — a skill absent from the JD (typescript here)
        // must come back with Emphasized == false, not merely "fewer bold than before."
        using var client = factory.CreateClient();

        const string jdText =
            "We need a backend engineer strong in C# and PostgreSQL. Requirements: production " +
            "experience with C# and PostgreSQL.";
        var jobId = await CreateJobAsync(client, jdText);

        using var baseResponse = await client.PostAsync("/api/resumes/base", content: null);
        (await baseResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options)).ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = false }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        var allSkills = result.Document.Skills.SelectMany(g => g.Items).ToList();
        var csharp = allSkills.Single(s => s.Normalized == "csharp");
        var typescript = allSkills.Single(s => s.Normalized == "typescript");

        csharp.Emphasized.ShouldBeTrue();
        typescript.Emphasized.ShouldBeFalse();
    }

    [Fact]
    public async Task Tailored_resume_from_a_raw_text_job_gets_a_human_readable_name_with_no_guid()
    {
        // Regression: a raw-text job posting (as opposed to one fetched from a URL) never
        // has Title/Company populated, so the naming fallback used to be
        // "Tailored - {job.Id}" — a bare GUID in a user-visible resume name. It must now
        // derive something readable from the posting's own text instead.
        using var client = factory.CreateClient();

        const string rawText = "Senior Backend Engineer at Nimbus Systems\n\nRequirements: 5+ years with C# and PostgreSQL.";
        var jobId = await CreateJobAsync(client, rawText);

        using var baseResponse = await client.PostAsync("/api/resumes/base", content: null);
        (await baseResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options)).ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = false }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.Document.Name.ShouldBe("Senior Backend Engineer at Nimbus Systems");
        result.Document.Name.ShouldNotContain(jobId);
        Guid.TryParse(result.Document.Name, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_posting_whose_opening_sentence_is_corporate_boilerplate_does_not_get_a_truncated_name()
    {
        // Regression: a real posting's raw text opened with "JT4, LLC provides engineering
        // and technical support to multiple western test ranges under the Test and
        // Training Range Contract." — corporate boilerplate, not a role title, and long
        // enough that the old naming fallback blindly character-truncated it into
        // "JT4, LLC provides engineering and technical support to multiple western test
        // ra…", a fragment chopped mid-word. It must now recognize the sentence isn't a
        // title and fall back to something sane instead — never that fragment, never a
        // truncated ellipsis, never a bare GUID.
        using var client = factory.CreateClient();

        const string rawText =
            "JT4, LLC provides engineering and technical support to multiple western test " +
            "ranges under the Test and Training Range Contract.\n\n" +
            "Requirements: 5+ years of experience with C# and PostgreSQL.";
        var jobId = await CreateJobAsync(client, rawText);

        using var baseResponse = await client.PostAsync("/api/resumes/base", content: null);
        (await baseResponse.Content.ReadFromJsonAsync<ResumeDocument>(TestJson.Options)).ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = false }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        const string reportedBrokenName =
            "JT4, LLC provides engineering and technical support to multiple western test ra…";

        result.Document.Name.ShouldNotBe(reportedBrokenName);
        result.Document.Name.ShouldNotEndWith("…");
        result.Document.Name.ShouldNotContain(jobId);
        Guid.TryParse(result.Document.Name, out _).ShouldBeFalse();
        result.Document.Name.ShouldContain("JT4");
    }

    [Fact]
    public async Task Tailoring_result_reports_a_page_count_and_fits_the_default_budget()
    {
        // End-to-end wiring check for CONTRACTS.md §6 "Page budget": the small fixture
        // profile easily fits the default two-page budget, so this only asserts that the
        // graph's enforce-page-budget node actually ran and surfaced a real page count —
        // never a specific page count for a specific content fixture, which typography
        // tuning could change out from under this test.
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.PageCount.ShouldBeGreaterThanOrEqualTo(1);
        result.FitsBudget.ShouldBeTrue();
    }

    [Fact]
    public async Task Max_pages_null_disables_page_budget_trimming_end_to_end()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, MaxPages = null, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.FitsBudget.ShouldBeTrue();
        result.Diff.ShouldNotContain(d => d.Rationale != null && d.Rationale.Contains("budget", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<string> CreateJobAsync(HttpClient client) => CreateJobAsync(client, SamplePosting);

    private static async Task<string> CreateJobAsync(HttpClient client, string rawText)
    {
        using var response = await client.PostAsJsonAsync("/api/jobs", new CreateJobRequest { RawText = rawText }, TestJson.Options);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        return posting!.Id;
    }
}
