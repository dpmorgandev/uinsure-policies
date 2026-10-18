using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The path dotnet run takes: Development, with the database configured only by the app's own files.
public sealed class DevelopmentConfigurationTests : IClassFixture<DevelopmentConfigurationApiFactory>
{
    private readonly DevelopmentConfigurationApiFactory _factory;

    public DevelopmentConfigurationTests(DevelopmentConfigurationApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_live_and_ready_return_200_Healthy_on_the_Development_configuration_alone()
    {
        using var client = _factory.CreateClient();

        using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
