using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The app's own limits with no override: writes 10 and global 100 per client IP per 60 s.
// No Rule trait: transport hardening.
public sealed class DefaultRateLimitsTests : IClassFixture<DefaultRateLimitsApiFactory>
{
    private readonly DefaultRateLimitsApiFactory _factory;

    public DefaultRateLimitsTests(DefaultRateLimitsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Default_writes_limit_lets_ten_posts_through_and_rejects_the_eleventh()
    {
        using var client = _factory.CreateHttpsClient();

        for (var request = 1; request <= 10; request++)
        {
            using var allowed = await client.SendOnce(TransportRequests.Sell(new SellPolicyRequestBuilder($"RL-DEF-W-{request}").Build()).From("198.51.100.50"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var rejected = await client.SendOnce(TransportRequests.Sell(new SellPolicyRequestBuilder("RL-DEF-W-11").Build()).From("198.51.100.50"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Default_global_limit_lets_a_hundred_requests_through_and_rejects_the_hundred_and_first()
    {
        using var client = _factory.CreateHttpsClient();

        for (var request = 1; request <= 100; request++)
        {
            using var allowed = await client.SendOnce(TransportRequests.Get("/policies/RL-DEF-G-1").From("198.51.100.51"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var rejected = await client.SendOnce(TransportRequests.Get("/policies/RL-DEF-G-1").From("198.51.100.51"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
