using Microsoft.AspNetCore.Hosting;

namespace Uinsure.Policies.Api.Tests.Support;

// Production with APPLICATIONINSIGHTS_CONNECTION_STRING set. The value is a dummy, not a secret: an
// all-zero key, and ingestion and live endpoints on a closed local port, so nothing leaves the machine.
public sealed class ApplicationInsightsApiFactory : ProductionApiFactory
{
    public const string DummyConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:1/;LiveEndpoint=http://127.0.0.1:1/";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Replaces the empty value the base factory sets; read by Program before builder.Build().
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", DummyConnectionString);
    }
}
