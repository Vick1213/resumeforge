using System.Diagnostics.CodeAnalysis;

// These integration tests run against an in-process WebApplicationFactory host with no
// external network calls and a short-lived HttpClient per test; threading
// TestContext.Current.CancellationToken through every one of the many HttpClient calls
// below would add substantial noise for negligible benefit in that setting.
[assembly: SuppressMessage(
    "Usage",
    "xUnit1051:Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken",
    Justification = "In-process test host with no external calls; see comment above.")]
