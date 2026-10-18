using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Transport row of the contract: a global per-client-IP fixed window and a stricter 'writes' policy on every
// POST, rejected with 429, Retry-After and /problems/rate-limited, /health/* exempt, no queue, and the client
// IP never logged. Limits here: global 5, writes 2, per 60 s (RateLimitingApiFactory). Each test uses its
// own client IP, because the window never resets inside a run. No Rule trait: transport hardening.
public sealed class RateLimitingTests : IClassFixture<RateLimitingApiFactory>
{
    private const string RateLimitedType = "/problems/rate-limited";

    private readonly RateLimitingApiFactory _factory;

    public RateLimitingTests(RateLimitingApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Third_post_from_one_client_inside_the_window_is_429_rate_limited_with_Retry_After()
    {
        using var client = _factory.CreateHttpsClient();

        using var first = await client.SendOnce(Sale("RL-WRITES-1").From("198.51.100.11"));
        using var second = await client.SendOnce(Sale("RL-WRITES-2").From("198.51.100.11"));
        using var third = await client.SendOnce(Sale("RL-WRITES-3").From("198.51.100.11"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        await ProblemAssert.TransportProblem(third, HttpStatusCode.TooManyRequests, "/policies", RateLimitedType);
        AssertRetryAfterWithinTheWindow(third);
    }

    [Fact]
    public async Task Sixth_request_from_one_client_inside_the_window_is_429_over_the_global_limit()
    {
        using var client = _factory.CreateHttpsClient();

        for (var request = 1; request <= RateLimitingApiFactory.GlobalLimit; request++)
        {
            using var allowed = await client.SendOnce(TransportRequests.Get("/policies/RL-GLOBAL-1").From("198.51.100.12"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var rejected = await client.SendOnce(TransportRequests.Get("/policies/RL-GLOBAL-1").From("198.51.100.12"));

        await ProblemAssert.TransportProblem(rejected, HttpStatusCode.TooManyRequests, "/policies/RL-GLOBAL-1", RateLimitedType);
        AssertRetryAfterWithinTheWindow(rejected);
    }

    [Fact]
    public async Task Health_endpoints_are_exempt_from_the_rate_limiter()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustGlobalLimit(client, "198.51.100.13");

        using var limited = await client.SendOnce(TransportRequests.Get("/policies/RL-HEALTH-1").From("198.51.100.13"));
        using var live = await client.SendOnce(TransportRequests.Get("/health/live").From("198.51.100.13"));
        using var ready = await client.SendOnce(TransportRequests.Get("/health/ready").From("198.51.100.13"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Another_client_is_not_limited_by_an_exhausted_client()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustWritesLimit(client, "198.51.100.10", "RL-OTHER");

        using var exhausted = await client.SendOnce(Sale("RL-OTHER-3").From("198.51.100.10"));
        using var other = await client.SendOnce(Sale("RL-OTHER-4").From("198.51.100.20"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    // Queue limit 0: the rejection comes at once, well inside the 60 s window, instead of waiting for a permit.
    [Fact]
    public async Task Rejected_request_is_answered_at_once_and_never_queued()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustWritesLimit(client, "198.51.100.14", "RL-QUEUE");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        using var rejected = await client.SendOnce(Sale("RL-QUEUE-3").From("198.51.100.14"), timeout.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    // A request with no remote IP falls into one fixed partition: it is limited, not a 500.
    [Fact]
    public async Task Request_without_a_remote_ip_is_still_limited()
    {
        using var client = _factory.CreateHttpsClient();

        using var first = await client.SendOnce(Sale("RL-NOIP-1"));
        using var second = await client.SendOnce(Sale("RL-NOIP-2"));
        using var third = await client.SendOnce(Sale("RL-NOIP-3"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        await ProblemAssert.TransportProblem(third, HttpStatusCode.TooManyRequests, "/policies", RateLimitedType);
    }

    // X-Forwarded-For is trusted only from a private-network proxy, so a public client can't spoof a fresh bucket.
    [Fact]
    public async Task Spoofed_forwarded_for_from_a_public_address_does_not_get_a_fresh_bucket()
    {
        using var client = _factory.CreateHttpsClient();

        using var first = await client.SendOnce(Sale("RL-SPOOF-1").From("203.0.113.7").WithHeader("X-Forwarded-For", "198.51.100.30"));
        using var second = await client.SendOnce(Sale("RL-SPOOF-2").From("203.0.113.7").WithHeader("X-Forwarded-For", "198.51.100.31"));
        using var third = await client.SendOnce(Sale("RL-SPOOF-3").From("203.0.113.7").WithHeader("X-Forwarded-For", "198.51.100.32"));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Rate_limited_client_ip_is_never_logged()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustWritesLimit(client, "203.0.113.77", "RL-LOG");

        using var rejected = await client.SendOnce(Sale("RL-LOG-3").From("203.0.113.77"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Message.Contains("203.0.113.77", StringComparison.Ordinal)
            || (entry.Exception?.ToString().Contains("203.0.113.77", StringComparison.Ordinal) ?? false));
    }

    // Every POST route carries the 'writes' policy, so cancel, claims and renew are guarded when they land.
    [Fact]
    public void Every_post_endpoint_carries_the_writes_policy()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var posts = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) ?? false)
            .ToArray();

        Assert.NotEmpty(posts);
        Assert.All(posts, endpoint =>
        {
            Assert.Equal("writes", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
            Assert.Null(endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>());
        });
    }

    private static HttpRequestMessage Sale(string reference) => TransportRequests.Sell(new SellPolicyRequestBuilder(reference).Build());

    private static async Task ExhaustWritesLimit(HttpClient client, string remoteIp, string referencePrefix)
    {
        for (var request = 1; request <= RateLimitingApiFactory.WritesLimit; request++)
        {
            using var allowed = await client.SendOnce(Sale($"{referencePrefix}-{request}").From(remoteIp));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }
    }

    private static async Task ExhaustGlobalLimit(HttpClient client, string remoteIp)
    {
        for (var request = 1; request <= RateLimitingApiFactory.GlobalLimit; request++)
        {
            using var allowed = await client.SendOnce(TransportRequests.Get("/policies/RL-EXHAUST-1").From(remoteIp));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }
    }

    // Retry-After as a whole number of seconds, inside the 60 s window.
    private static void AssertRetryAfterWithinTheWindow(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values), "The 429 has no Retry-After header.");
        var seconds = int.Parse(Assert.Single(values), NumberStyles.None, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, 60);
    }
}
