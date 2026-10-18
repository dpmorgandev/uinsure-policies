using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Persistence;

namespace Uinsure.Policies.Api.Tests.Support;

// The API in-process on one open SQLite in-memory connection, with a FakeTimeProvider at TestClock.Now.
// One factory per test class (IClassFixture).
public class PoliciesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeTimeProvider Clock { get; } = new(TestClock.Now);

    // The service registrations the app made, captured after Program ran.
    public IReadOnlyList<Type> RegisteredServiceTypes { get; private set; } = [];

    protected virtual string EnvironmentName => Environments.Development;

    // AdjustTime, not SetUtcNow: a test may have moved the clock forward, and SetUtcNow can't go back.
    public void ResetClock() => Clock.AdjustTime(TestClock.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Policies", "DataSource=:memory:");
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", string.Empty);
        ConfigureRateLimits(builder);

        builder.ConfigureTestServices(services =>
        {
            RegisteredServiceTypes = [.. services.Select(descriptor => descriptor.ServiceType)];

            services.RemoveAll<DbContextOptions<PoliciesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PoliciesDbContext>>();
            services.AddDbContext<PoliciesDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    // Every in-process request shares one client partition and one factory serves a whole test class, so
    // the suites raise both limits far above any class's request count. RateLimitingApiFactory sets its
    // own low limits; DefaultRateLimitsApiFactory sets none, so the app's defaults apply.
    protected virtual void ConfigureRateLimits(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimiting:Global:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Global:Window", "00:01:00");
        builder.UseSetting("RateLimiting:Writes:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:Writes:Window", "00:01:00");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Open before the host starts: an in-memory database lives only while its connection is open.
        // Program's start-up EnsureCreated then builds the schema on this connection.
        _connection.Open();
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
