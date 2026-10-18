using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The on branch of the Application Insights switch. With ApplicationInsightsTests (the off branch),
// this tells "wired only when the connection string is set" apart from "never wired".
public sealed class ApplicationInsightsEnabledTests : IClassFixture<ApplicationInsightsApiFactory>
{
    private readonly ApplicationInsightsApiFactory _factory;

    public ApplicationInsightsEnabledTests(ApplicationInsightsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public void OpenTelemetry_and_Azure_Monitor_services_are_registered_with_a_connection_string()
    {
        _ = _factory.Services;

        Assert.Contains(_factory.RegisteredServiceTypes, IsTelemetryType);
    }

    [Fact]
    public async Task App_starts_and_answers_health_with_an_Application_Insights_connection_string()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static bool IsTelemetryType(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("OpenTelemetry", StringComparison.Ordinal) || ns.StartsWith("Azure.Monitor", StringComparison.Ordinal);
    }
}
