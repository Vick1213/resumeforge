using System.Net;
using System.Text;
using ResumeForge.Infrastructure.Jobs;
using ResumeForge.Infrastructure.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Jobs;

/// <summary>Tests for <see cref="JobPostingFetcher"/> against a stubbed <see cref="HttpMessageHandler"/> (no network).</summary>
public sealed class JobPostingFetcherTests
{
    private static JobPostingFetcher NewFetcher(HttpResponseMessage response, FixedTimeProvider? time = null)
    {
        var handler = new StubHttpMessageHandler(_ => response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        return new JobPostingFetcher(new StubHttpClientFactory(client), time ?? new FixedTimeProvider(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task Successful_fetch_extracts_text_and_metadata()
    {
        const string html = """
            <html><head><meta property="og:title" content="Backend Engineer"></head>
            <body><main><h1>Backend Engineer</h1><p>Build things.</p></main></body></html>
            """;

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
        var fetcher = NewFetcher(response);

        var posting = await fetcher.FetchAsync("https://example.com/jobs/1", CancellationToken.None);

        posting.SourceUrl.ShouldBe("https://example.com/jobs/1");
        posting.Title.ShouldBe("Backend Engineer");
        posting.RawText.ShouldContain("Build things.");
        posting.Id.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Non_success_status_throws_a_catchable_fetch_exception()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
        var fetcher = NewFetcher(response);

        await Should.ThrowAsync<JobPostingFetchException>(() => fetcher.FetchAsync("https://example.com/jobs/missing", CancellationToken.None));
    }

    [Fact]
    public async Task A_page_with_no_readable_text_throws_a_fetch_exception()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><head><script>x()</script></head><body></body></html>") };
        var fetcher = NewFetcher(response);

        await Should.ThrowAsync<JobPostingFetchException>(() => fetcher.FetchAsync("https://example.com/empty", CancellationToken.None));
    }
}
