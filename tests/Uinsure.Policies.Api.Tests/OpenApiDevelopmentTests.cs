using System.Net;
using System.Text.Json;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

public sealed class OpenApiDevelopmentTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public OpenApiDevelopmentTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task OpenApi_document_is_served_as_OpenAPI_3_1_in_Development()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("3.1", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scalar_api_reference_is_served_in_Development()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
