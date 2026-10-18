using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Uinsure.Policies.Api.Tests.Support;

// Production with no RateLimiting override, so the app's own defaults apply, and a settable client IP.
public sealed class DefaultRateLimitsApiFactory : ProductionApiFactory
{
    protected override void ConfigureRateLimits(IWebHostBuilder builder)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, RemoteIpStartupFilter>());
    }
}
