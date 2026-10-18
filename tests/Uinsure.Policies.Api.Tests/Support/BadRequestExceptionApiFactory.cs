using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Uinsure.Policies.Api.Tests.Support;

// Production, plus test-only paths that throw BadHttpRequestException, and a logger provider that captures
// what is logged. Clients use HTTPS, so requests pass the HTTPS check and reach the throwing paths.
public class BadRequestExceptionApiFactory : ProductionApiFactory
{
    public BadRequestExceptionApiFactory() => ClientOptions.BaseAddress = TransportClients.HttpsBaseAddress;

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, BadRequestEndpointStartupFilter>();
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
