using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Uinsure.Policies.Api.Tests.Support;

// Production with low limits (global 5, writes 2, both per 60 s), a settable client IP and captured logs.
// The limiter keeps its own clock, not TimeProvider, so the window is never crossed: each test uses its
// own client IP.
public sealed class RateLimitingApiFactory : ProductionApiFactory
{
    public const int GlobalLimit = 5;

    public const int WritesLimit = 2;

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureRateLimits(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimiting:Global:PermitLimit", GlobalLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:Global:Window", "00:01:00");
        builder.UseSetting("RateLimiting:Writes:PermitLimit", WritesLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:Writes:Window", "00:01:00");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, RemoteIpStartupFilter>();
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
