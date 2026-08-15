using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;

namespace ResumeForge.Infrastructure.Jobs;

/// <summary>
/// <see cref="IJobPostingFetcher"/> over <see cref="HttpClient"/>, using
/// <see cref="HtmlTextExtractor"/> to pull readable text and metadata out of the fetched
/// page. A fetch failure or a page with no readable text throws
/// <see cref="JobPostingFetchException"/>, which the API maps to a <c>ProblemDetails</c> response.
/// </summary>
public sealed class JobPostingFetcher(IHttpClientFactory httpClientFactory, TimeProvider timeProvider) : IJobPostingFetcher
{
    /// <summary>The name registered for this fetcher's <see cref="IHttpClientFactory"/> client.</summary>
    public const string HttpClientName = "job-posting-fetcher";

    /// <inheritdoc />
    public async Task<JobPosting> FetchAsync(string url, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var client = httpClientFactory.CreateClient(HttpClientName);
        string html;

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new JobPostingFetchException(url, $"server returned HTTP {(int)response.StatusCode}.");
            }

            html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new JobPostingFetchException(url, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new JobPostingFetchException(url, "the request timed out.", ex);
        }

        var text = HtmlTextExtractor.ExtractReadableText(html);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JobPostingFetchException(url, "no readable text was found on the page.");
        }

        var metadata = HtmlTextExtractor.ExtractMetadata(html);

        return new JobPosting
        {
            Id = Guid.NewGuid().ToString(),
            SourceUrl = url,
            Company = metadata.Company,
            Title = metadata.Title,
            Location = metadata.Location,
            RawText = text,
            FetchedAt = timeProvider.GetUtcNow(),
        };
    }
}
