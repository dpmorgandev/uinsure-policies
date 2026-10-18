using System.Text;
using Microsoft.Extensions.Logging;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Transport row of the contract: a request the client aborts (an OperationCanceledException while
// RequestAborted is cancelled) is not a server fault. Nobody is there to read a body, so no 500 problem body
// is written, and it is logged at Information, never at Error. The sale goes through the real controller,
// service and repository; only the repository's lookup is made to wait for the abort.
// On the F1 code ASP.NET Core's ExceptionHandlerMiddleware already ends an aborted request with 499 and logs it
// at Debug, before any IExceptionHandler runs: the no-500 and never-Error tests guard that, and the app's own
// Information entry is the missing behaviour.
// No Rule trait: transport hardening is not a numbered rule.
public sealed class ClientAbortedRequestTests : IClassFixture<ClientAbortedApiFactory>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ClientAbortedApiFactory _factory;

    public ClientAbortedRequestTests(ClientAbortedApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Sale_aborted_by_the_client_gets_no_500_problem_body()
    {
        var served = await SellAndAbort("ABORT-NO-500");

        Assert.True(served.Aborted, "The request was expected to end aborted.");
        Assert.NotEqual(500, served.Status);
        Assert.False(
            served.ContentType?.StartsWith("application/problem+json", StringComparison.Ordinal) == true,
            $"A problem body was written for an aborted request: {served.Status} {served.ContentType}.");
    }

    [Fact]
    public async Task Sale_aborted_by_the_client_is_never_logged_at_Error()
    {
        await SellAndAbort("ABORT-NOT-ERROR");

        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Level >= LogLevel.Error && (entry.Exception is OperationCanceledException || entry.Message.Contains("/policies", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sale_aborted_by_the_client_is_logged_at_Information_by_the_app()
    {
        await SellAndAbort("ABORT-INFORMATION");

        Assert.Contains(_factory.Logs.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Category.StartsWith("Uinsure.Policies", StringComparison.Ordinal)
            && entry.Message.Contains("/policies", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sale_aborted_by_the_client_is_not_stored()
    {
        await SellAndAbort("ABORT-NOT-STORED");

        using var client = _factory.CreateClient();
        await client.AssertNotStored("ABORT-NOT-STORED");
    }

    // POST a valid sale, wait until its lookup is in flight, then abort it as a client that goes away does.
    // Returns what the server ended the request with.
    private async Task<ServedRequest> SellAndAbort(string reference)
    {
        var ct = TestContext.Current.CancellationToken;
        var lookupWaiting = _factory.ArmLookup(reference);
        var served = _factory.Served(reference);

        using var client = _factory.CreateClient();
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/policies")
        {
            Content = new StringContent(new SellPolicyRequestBuilder(reference).Build().ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ClientAbortedApiFactory.ProbeHeader, reference);

        var sending = client.SendAsync(request, abort.Token);
        await lookupWaiting.WaitAsync(Patience, ct);
        await abort.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        return await served.WaitAsync(Patience, ct);
    }
}
