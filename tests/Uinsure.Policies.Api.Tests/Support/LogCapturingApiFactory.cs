using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Uinsure.Policies.Api.Tests.Support;

// Production, with nothing added but a logger provider that captures what is logged, so a test can show that
// an ordinary request writes no Error-level entry. Clients use HTTPS, so requests pass the HTTPS check.
public sealed class LogCapturingApiFactory : ProductionApiFactory
{
    public LogCapturingApiFactory() => ClientOptions.BaseAddress = TransportClients.HttpsBaseAddress;

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(Logs));
    }
}
