using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Transport row of the contract: outside Development, plain HTTP is rejected with 400 /problems/https-required
// (never redirected), /health/* is exempt, and HTTPS responses carry HSTS. Development serves both.
// No Rule trait: transport hardening is not a numbered rule.
public sealed class HttpsTransportTests : IClassFixture<ProductionApiFactory>, IClassFixture<PoliciesApiFactory>
{
    private const string HttpsRequiredType = "/problems/https-required";

    private readonly ProductionApiFactory _production;
    private readonly PoliciesApiFactory _development;

    public HttpsTransportTests(ProductionApiFactory production, PoliciesApiFactory development)
    {
        _production = production;
        _development = development;
        _production.ResetClock();
        _development.ResetClock();
    }

    [Fact]
    public async Task Get_over_plain_http_outside_Development_is_400_https_required()
    {
        using var client = _production.CreateHttpClient();

        using var response = await client.GetPolicy("HTTPS-GET-1");

        await ProblemAssert.TransportProblem(response, HttpStatusCode.BadRequest, "/policies/HTTPS-GET-1", HttpsRequiredType);
    }

    [Fact]
    public async Task Post_over_plain_http_outside_Development_is_400_https_required_before_any_controller_runs()
    {
        using var client = _production.CreateHttpClient();

        using var response = await client.PostPolicy(new SellPolicyRequestBuilder("HTTPS-POST-1").Build());

        await ProblemAssert.TransportProblem(response, HttpStatusCode.BadRequest, "/policies", HttpsRequiredType);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Health_endpoints_are_exempt_from_the_https_check()
    {
        using var client = _production.CreateHttpClient();

        using var policy = await client.GetPolicy("HTTPS-HEALTH-1");
        using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        await ProblemAssert.TransportProblem(policy, HttpStatusCode.BadRequest, "/policies/HTTPS-HEALTH-1", HttpsRequiredType);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Plain_http_is_rejected_outside_Development_only()
    {
        using var productionClient = _production.CreateHttpClient();
        using var developmentClient = _development.CreateHttpClient();

        using var production = await productionClient.GetPolicy("HTTPS-ENV-1");
        using var development = await developmentClient.GetPolicy("HTTPS-ENV-1");

        await ProblemAssert.TransportProblem(production, HttpStatusCode.BadRequest, "/policies/HTTPS-ENV-1", HttpsRequiredType);
        Assert.NotEqual(HttpStatusCode.BadRequest, development.StatusCode);
    }

    [Fact]
    public async Task Https_response_outside_Development_carries_Strict_Transport_Security()
    {
        using var client = _production.CreateHttpsClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"), "No Strict-Transport-Security header outside Development.");
    }

    [Fact]
    public async Task Https_response_carries_Strict_Transport_Security_outside_Development_only()
    {
        using var productionClient = _production.CreateHttpsClient();
        using var developmentClient = _development.CreateHttpsClient();

        using var production = await productionClient.GetAsync("/health/live", TestContext.Current.CancellationToken);
        using var development = await developmentClient.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.True(production.Headers.Contains("Strict-Transport-Security"), "No Strict-Transport-Security header outside Development.");
        Assert.False(development.Headers.Contains("Strict-Transport-Security"), "Development sent Strict-Transport-Security.");
    }
}
