using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Uinsure.Policies.Api.Tests.Support;

// The API in Development on the project's own configuration: nothing is injected for the database,
// so the provider and connection come from appsettings*.json exactly as they do under dotnet run.
// Only the clock is swapped. The SQLite file the configuration names is deleted on dispose.
public sealed class DevelopmentConfigurationApiFactory : WebApplicationFactory<Program>
{
    private string? _databaseFile;

    public FakeTimeProvider Clock { get; } = new(TestClock.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        var connectionString = host.Services.GetRequiredService<IConfiguration>().GetConnectionString("Policies");
        _databaseFile = DatabaseFileToRemove(connectionString);

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _databaseFile is not null)
        {
            // Pooled connections keep the file open on Windows.
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _databaseFile, _databaseFile + "-shm", _databaseFile + "-wal" })
            {
                File.Delete(file);
            }
        }
    }

    // Only a file-backed SQLite database under this test run's own directories is removed, so a
    // misconfigured path can never delete a database the test didn't create.
    private static string? DatabaseFileToRemove(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(dataSource, Environment.CurrentDirectory);
        var ownDirectories = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }
            .Select(directory => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar);

        return ownDirectories.Any(directory => fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            ? fullPath
            : null;
    }
}
