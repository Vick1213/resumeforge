namespace ResumeForge.Api.Tests.TestSupport;

/// <summary>An <see cref="HttpMessageHandler"/> test double that returns a fixed response, no network involved.</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
