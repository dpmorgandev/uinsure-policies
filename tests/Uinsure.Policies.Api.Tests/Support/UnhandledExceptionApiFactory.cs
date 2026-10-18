using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Uinsure.Policies.Api.Tests.Support;

// Production, plus a test-only path that throws, and a logger provider that captures what is logged.
public sealed class UnhandledExceptionApiFactory : ProductionApiFactory
{
    // HTTPS, so the request passes the HTTPS check outside Development and reaches the throwing path.
    public UnhandledExceptionApiFactory() => ClientOptions.BaseAddress = TransportClients.HttpsBaseAddress;

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>();
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
