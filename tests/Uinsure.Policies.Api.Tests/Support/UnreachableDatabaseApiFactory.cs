using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Uinsure.Policies.Api.Tests.Support;

// The app's own SQL Server provider pointed at a closed local port, so the ready check has a
// database it can't reach. No SQLite override: this is the configured provider switch.
public sealed class UnreachableDatabaseApiFactory : WebApplicationFactory<Program>
{
    private const string UnreachableSqlServer =
        "Server=tcp:127.0.0.1,1;Database=Policies;Connect Timeout=2;ConnectRetryCount=0;Encrypt=False";

    public FakeTimeProvider Clock { get; } = new(TestClock.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Database:Provider", "SqlServer");
        builder.UseSetting("ConnectionStrings:Policies", UnreachableSqlServer);
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
