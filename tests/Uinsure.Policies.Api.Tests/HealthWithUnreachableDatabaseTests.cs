using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Ready runs the database check and live runs none, so only ready notices the database is gone.
public sealed class HealthWithUnreachableDatabaseTests : IClassFixture<UnreachableDatabaseApiFactory>
{
    private readonly UnreachableDatabaseApiFactory _factory;

    public HealthWithUnreachableDatabaseTests(UnreachableDatabaseApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ready_returns_503_Unhealthy_when_the_database_cannot_be_reached()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_live_returns_200_Healthy_when_the_database_cannot_be_reached()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
