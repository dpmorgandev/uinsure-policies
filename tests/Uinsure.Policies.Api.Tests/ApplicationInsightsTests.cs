using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Azure Monitor is wired only when APPLICATIONINSIGHTS_CONNECTION_STRING is set. Without it, the app
// still starts (in Production, where it would otherwise be used) and registers no telemetry exporter.
public sealed class ApplicationInsightsTests : IClassFixture<ProductionApiFactory>
{
    private readonly ProductionApiFactory _factory;

    public ApplicationInsightsTests(ProductionApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task App_starts_and_answers_health_without_an_Application_Insights_connection_string()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void No_OpenTelemetry_or_Azure_Monitor_service_is_registered_without_a_connection_string()
    {
        _ = _factory.Services;

        Assert.DoesNotContain(_factory.RegisteredServiceTypes, IsTelemetryType);
    }

    private static bool IsTelemetryType(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("OpenTelemetry", StringComparison.Ordinal) || ns.StartsWith("Azure.Monitor", StringComparison.Ordinal);
    }
}
