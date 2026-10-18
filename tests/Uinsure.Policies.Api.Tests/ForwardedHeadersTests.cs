using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Forwarded headers: X-Forwarded-For and X-Forwarded-Proto are trusted only from 10.0.0.0/8, 172.16.0.0/12
// and 192.168.0.0/16 (App Service's balancer), with a forward limit of 1. Production, so the HTTPS check
// shows whether X-Forwarded-Proto was applied, and the rate limiter shows which client IP was used.
// No Rule trait: transport hardening.
public sealed class ForwardedHeadersTests : IClassFixture<RateLimitingApiFactory>
{
    private const string HttpsRequiredType = "/problems/https-required";

    private readonly RateLimitingApiFactory _factory;

    public ForwardedHeadersTests(RateLimitingApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // The control request without the header shows plain HTTP from that proxy is rejected, so the header
    // is what lets the forwarded request through.
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.254")]
    public async Task Forwarded_https_from_a_private_network_proxy_passes_the_https_check(string proxy)
    {
        using var client = _factory.CreateHttpClient();

        using var direct = await client.SendOnce(TransportRequests.Get("/policies/FWD-PROXY-1").From(proxy));
        using var forwarded = await client.SendOnce(TransportRequests.Get("/policies/FWD-PROXY-1").From(proxy).WithHeader("X-Forwarded-Proto", "https"));

        await ProblemAssert.TransportProblem(direct, HttpStatusCode.BadRequest, "/policies/FWD-PROXY-1", HttpsRequiredType);
        Assert.NotEqual(HttpStatusCode.BadRequest, forwarded.StatusCode);
    }

    // Public addresses, the addresses just outside each private network, and loopback (the framework's
    // default trusted proxy) are all untrusted, so X-Forwarded-Proto is ignored.
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("11.0.0.1")]
    [InlineData("172.15.255.254")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Forwarded_https_from_an_untrusted_address_is_ignored(string remoteIp)
    {
        using var client = _factory.CreateHttpClient();

        using var response = await client.SendOnce(TransportRequests.Get("/policies/FWD-UNTRUSTED-1").From(remoteIp).WithHeader("X-Forwarded-Proto", "https"));

        await ProblemAssert.TransportProblem(response, HttpStatusCode.BadRequest, "/policies/FWD-UNTRUSTED-1", HttpsRequiredType);
    }

    [Fact]
    public async Task Clients_behind_a_trusted_proxy_get_their_own_rate_limit_buckets()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustWritesLimit(client, "10.0.0.1", "198.51.100.40", "FWD-BUCKET");

        using var exhausted = await client.SendOnce(Sale("FWD-BUCKET-3").From("10.0.0.1").WithHeader("X-Forwarded-For", "198.51.100.40"));
        using var other = await client.SendOnce(Sale("FWD-BUCKET-4").From("10.0.0.1").WithHeader("X-Forwarded-For", "198.51.100.41"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    // Forward limit 1: only the entry the trusted proxy added (the last) is used. The chain's last entry is
    // itself a private address, so a limit above 1 would walk on to 198.51.100.42 and share its bucket.
    [Fact]
    public async Task Only_the_nearest_forwarded_for_entry_is_used_as_the_client_ip()
    {
        using var client = _factory.CreateHttpsClient();
        await ExhaustWritesLimit(client, "10.0.0.1", "198.51.100.42, 10.0.0.2", "FWD-LIMIT");

        using var exhausted = await client.SendOnce(Sale("FWD-LIMIT-3").From("10.0.0.1").WithHeader("X-Forwarded-For", "198.51.100.42, 10.0.0.2"));
        using var firstHop = await client.SendOnce(Sale("FWD-LIMIT-4").From("10.0.0.1").WithHeader("X-Forwarded-For", "198.51.100.42"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstHop.StatusCode);
    }

    [Fact]
    public void Forwarded_headers_options_trust_exactly_the_three_private_networks_and_no_proxy()
    {
        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(
            ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"],
            options.KnownIPNetworks.Select(network => network.ToString()).Order(StringComparer.Ordinal));
    }

    private static HttpRequestMessage Sale(string reference) => TransportRequests.Sell(new SellPolicyRequestBuilder(reference).Build());

    private static async Task ExhaustWritesLimit(HttpClient client, string proxy, string forwardedFor, string referencePrefix)
    {
        for (var request = 1; request <= RateLimitingApiFactory.WritesLimit; request++)
        {
            using var allowed = await client.SendOnce(Sale($"{referencePrefix}-{request}").From(proxy).WithHeader("X-Forwarded-For", forwardedFor));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }
    }
}
