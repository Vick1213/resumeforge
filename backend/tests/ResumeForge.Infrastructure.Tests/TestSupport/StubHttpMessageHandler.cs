namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>An <see cref="HttpMessageHandler"/> test double that returns a fixed response, no network involved.</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

/// <summary>An <see cref="IHttpClientFactory"/> test double that always returns one pre-built client.</summary>
public sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
