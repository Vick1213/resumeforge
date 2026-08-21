using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
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
    public async Task A_run_reports_the_ats_review_and_the_score_closing_its_gaps_would_reach()
    {
        // End to end on the offline heuristic provider, which the factory pins: the
        // ats-review node runs, its findings reach build-brief, and the result carries them
        // out to the client (CONTRACTS.md §6 "ATS review pass").
        using var client = factory.CreateClient();
        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, Effort = ModelEffort.Standard, DryRun = true },
            TestJson.Options);

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);

        result!.AtsReview.ShouldNotBeNull();
        result.AtsReview.ScoreAfter.ShouldBeGreaterThanOrEqualTo(result.AtsReview.ScoreBefore);
        result.AtsReview.Verdict.ShouldNotBeNullOrWhiteSpace();
        result.Trace.ShouldContain(t => t.Node == "ats-review" && t.Status == GraphNodeStatus.Succeeded);
    }

    [Fact]
    public async Task Minimal_effort_skips_the_ats_review_because_it_cannot_act_on_it()
    {
        // MaxRewrites is 0 at Minimal, so a report whose whole output is "work these terms
        // into these bullets" would name work the command pass is forbidden to do.
        using var client = factory.CreateClient();
        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, Effort = ModelEffort.Minimal, DryRun = true },
            TestJson.Options);

        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);

        result!.AtsReview.ShouldBeNull();
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
    public async Task Tailored_resume_headline_becomes_the_job_title_when_one_was_determined_at_ingest()
    {
        // CONTRACTS.md §2 "Tailored headline": deterministic, never a model decision.
        // POST /api/jobs never sets Title for a pasted rawText posting (only a
        // URL-fetched one does, via HtmlTextExtractor's JSON-LD/OpenGraph parsing), so this
        // substitutes IJobPostingFetcher the same way the malformed-command test above
        // substitutes ILanguageModel, and gives the title messy whitespace to also prove
        // the override normalizes it rather than copying it verbatim.
        var fetcher = Substitute.For<IJobPostingFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new JobPosting
        {
            Id = Guid.NewGuid().ToString(),
            SourceUrl = "https://example.com/job",
            Title = "  Senior   Backend Engineer  ",
            RawText = SamplePosting,
            FetchedAt = DateTimeOffset.UtcNow,
        }));

        using var scopedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<IJobPostingFetcher>(_ => fetcher)));
        using var client = scopedFactory.CreateClient();

        using var jobResponse = await client.PostAsJsonAsync(
            "/api/jobs", new CreateJobRequest { Url = "https://example.com/job" }, TestJson.Options);
        var job = await jobResponse.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        job.ShouldNotBeNull();

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = job.Id, DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.Document.Basics.Headline.ShouldBe("Senior Backend Engineer");
    }

    [Fact]
    public async Task A_custom_headline_on_the_request_wins_over_the_job_title()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, Headline = "  Embedded   Systems Engineer ", DryRun = true },
            TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        // Trimmed and whitespace-collapsed, exactly as the job-title path normalizes.
        result.Document.Basics.Headline.ShouldBe("Embedded Systems Engineer");
    }

    [Fact]
    public async Task A_blank_custom_headline_falls_back_to_the_job_title_default()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor", new TailorRequest { JobId = jobId, Headline = "   ", DryRun = true }, TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.Document.Basics.Headline.ShouldNotBeNullOrWhiteSpace();
        result.Document.Basics.Headline.ShouldNotContain("Embedded");
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

    [Fact]
    public async Task Max_pages_one_trims_more_than_the_default_two_page_budget_end_to_end()
    {
        // Regression for the bug this change fixes: TailoringGraphFactory.Create's options
        // merge copied Effort and MaxRewrites from the request but forgot MaxPages, so
        // enforce-page-budget always saw the DI-configured TailorOptions default (2)
        // regardless of what TailorRequest.MaxPages asked for — a live run of
        // `maxPages: 1` used to come back as a 2-page document with fitsBudget: true.
        // The shared fixture profile is too small to ever need trimming at all (see
        // Tailoring_result_reports_a_page_count_and_fits_the_default_budget above), so this
        // temporarily adds enough knowledge items to overflow the default two-page budget
        // on its own, and removes them again in `finally` so it never leaks into any other
        // test sharing this collection's knowledge base.
        using var client = factory.CreateClient();

        var scratchIds = await AddLargeFillerKnowledgeBaseAsync(client);
        try
        {
            var jobId = await CreateJobAsync(client);

            using var twoPageResponse = await client.PostAsJsonAsync(
                "/api/tailor", new TailorRequest { JobId = jobId, MaxPages = 2, DryRun = true }, TestJson.Options);
            twoPageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var twoPageResult = await twoPageResponse.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
            twoPageResult.ShouldNotBeNull();

            using var onePageResponse = await client.PostAsJsonAsync(
                "/api/tailor", new TailorRequest { JobId = jobId, MaxPages = 1, DryRun = true }, TestJson.Options);
            onePageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var onePageResult = await onePageResponse.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
            onePageResult.ShouldNotBeNull();

            // The bug under regression would have made this come back PageCount: 2,
            // FitsBudget: true — the request's MaxPages: 1 silently ignored.
            onePageResult.PageCount.ShouldBe(1);
            onePageResult.FitsBudget.ShouldBeTrue();

            IncludedEntryCount(onePageResult.Document).ShouldBeLessThan(IncludedEntryCount(twoPageResult.Document));

            // Employment history is never surrendered to a page budget: every experience
            // entry survives even the tightest budget.
            onePageResult.Document.Experience.ShouldAllBe(e => e.Included);
        }
        finally
        {
            foreach (var id in scratchIds)
            {
                await client.DeleteAsync($"/api/knowledge/{id}");
            }
        }
    }

    private static int IncludedEntryCount(ResumeDocument document) =>
        document.Experience.Count(e => e.Included) +
        document.Projects.Count(p => p.Included) +
        document.Certifications.Count(c => c.Included);

    /// <summary>
    /// Writes a batch of low-relevance filler project knowledge items — enough that the
    /// rendered resume overflows the default two-page budget on its own — and returns
    /// their ids so the caller can delete them again once done. Projects only: experience
    /// entries are never cut by the page-budget enforcer, so filler that overflowed the
    /// page via experience would make a one-page budget unmeetable by design. Every
    /// entry's <c>tech:</c> is deliberately unrelated to <see cref="SamplePosting"/>'s
    /// C#/.NET/PostgreSQL/Kubernetes requirements, so each one scores at or near zero
    /// relevance and is a preferred cut candidate ahead of the fixture's own real entries.
    /// </summary>
    private static async Task<List<string>> AddLargeFillerKnowledgeBaseAsync(HttpClient client)
    {
        var ids = new List<string>();

        for (var i = 0; i < 16; i++)
        {
            var id = $"prj:scratch-filler-{i:D2}";
            var markdown = $"""
                ---
                type: project
                name: Filler Side Project {i}
                tagline: An unrelated filler project
                startDate: {2003 + i}-01
                endDate: {2004 + i}-01
                tech: [Ruby, MongoDB]
                tags: [filler]
                ---

                - Did unrelated filler work item one for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                - Did unrelated filler work item two for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                - Did unrelated filler work item three for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                - Did unrelated filler work item four for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                - Did unrelated filler work item five for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                - Did unrelated filler work item six for project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                """;

            using var response = await client.PutAsJsonAsync(
                $"/api/knowledge/{id}", new UpsertKnowledgeRequest { RawMarkdown = markdown }, TestJson.Options);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            ids.Add(id);
        }

        for (var i = 0; i < 10; i++)
        {
            var id = $"prj:scratch-page-budget-{i:D2}";
            var markdown = $"""
                ---
                type: project
                name: Filler Project {i}
                tagline: An unrelated filler project
                startDate: {2010 + i}-01
                endDate: {2011 + i}-01
                tech: [Ruby, MongoDB]
                tags: [filler]
                source: manual
                ---

                - Built unrelated filler project {i}, padding the rendered length of this entry so the resume overflows the default page budget in this test.
                """;

            using var response = await client.PutAsJsonAsync(
                $"/api/knowledge/{id}", new UpsertKnowledgeRequest { RawMarkdown = markdown }, TestJson.Options);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            ids.Add(id);
        }

        return ids;
    }

    [Fact]
    public async Task Excluded_entry_ids_are_forced_out_of_the_tailored_resume_end_to_end()
    {
        // CONTRACTS.md §6 "Forced inclusion": the fixture's one experience entry
        // (exp:acme-corp) is squarely on-topic for SamplePosting (C#/.NET/PostgreSQL), so
        // the heuristic model would include it on its own — excludedEntryIds must override
        // that decision after command execution regardless.
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, ExcludedEntryIds = ["exp:acme-corp"], DryRun = true },
            TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
        result.ShouldNotBeNull();

        result.Document.Experience.Single(e => e.Id == "exp:acme-corp").Included.ShouldBeFalse();
    }

    [Fact]
    public async Task Pinned_entry_ids_survive_the_page_budget_trim_end_to_end()
    {
        // CONTRACTS.md §6 "Forced inclusion" + "Page budget": pins extend the never-cut
        // floor. Adds enough low-relevance filler (same helper Max_pages_one_trims... uses)
        // to force enforce-page-budget to actually cut entries at maxPages: 1, pins one of
        // the low-relevance filler projects, and asserts it survives while its unpinned,
        // equally low-relevance siblings do not.
        using var client = factory.CreateClient();

        var scratchIds = await AddLargeFillerKnowledgeBaseAsync(client);
        try
        {
            var jobId = await CreateJobAsync(client);
            const string pinnedId = "prj:scratch-page-budget-00";

            using var response = await client.PostAsJsonAsync(
                "/api/tailor",
                new TailorRequest { JobId = jobId, MaxPages = 1, PinnedEntryIds = [pinnedId], DryRun = true },
                TestJson.Options);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<TailoringResult>(TestJson.Options);
            result.ShouldNotBeNull();

            result.Document.Projects.Single(p => p.Id == pinnedId).Included.ShouldBeTrue();
            result.Document.Projects.Where(p => p.Id != pinnedId && p.Id.StartsWith("prj:scratch-page-budget-", StringComparison.Ordinal))
                .ShouldContain(p => !p.Included);
        }
        finally
        {
            foreach (var id in scratchIds)
            {
                await client.DeleteAsync($"/api/knowledge/{id}");
            }
        }
    }

    [Fact]
    public async Task Tailor_with_an_unknown_pinned_entry_id_returns_400_naming_it()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, PinnedEntryIds = ["prj:does-not-exist"], DryRun = true },
            TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        problem.ShouldNotBeNull();
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
        problem.Detail.ShouldContain("prj:does-not-exist");
    }

    [Fact]
    public async Task Tailor_with_an_unknown_excluded_entry_id_returns_400_naming_it()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest { JobId = jobId, ExcludedEntryIds = ["exp:does-not-exist"], DryRun = true },
            TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        problem.ShouldNotBeNull();
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
        problem.Detail.ShouldContain("exp:does-not-exist");
    }

    [Fact]
    public async Task Tailor_with_an_id_in_both_pinned_and_excluded_lists_returns_400()
    {
        using var client = factory.CreateClient();

        var jobId = await CreateJobAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/tailor",
            new TailorRequest
            {
                JobId = jobId,
                PinnedEntryIds = ["prj:graph-runner"],
                ExcludedEntryIds = ["prj:graph-runner"],
                DryRun = true,
            },
            TestJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        problem.ShouldNotBeNull();
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
        problem.Detail.ShouldContain("prj:graph-runner");
    }

    private static Task<string> CreateJobAsync(HttpClient client) => CreateJobAsync(client, SamplePosting);

    private static async Task<string> CreateJobAsync(HttpClient client, string rawText)
    {
        using var response = await client.PostAsJsonAsync("/api/jobs", new CreateJobRequest { RawText = rawText }, TestJson.Options);
        var posting = await response.Content.ReadFromJsonAsync<JobPosting>(TestJson.Options);
        return posting!.Id;
    }
}
