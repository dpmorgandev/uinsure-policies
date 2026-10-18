using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

public sealed class OpenApiProductionTests : IClassFixture<ProductionApiFactory>
{
    private readonly ProductionApiFactory _factory;

    public OpenApiProductionTests(ProductionApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task OpenApi_document_is_not_served_outside_Development()
    {
        using var client = _factory.CreateHttpsClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scalar_api_reference_is_not_served_outside_Development()
    {
        using var client = _factory.CreateHttpsClient();

        using var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
