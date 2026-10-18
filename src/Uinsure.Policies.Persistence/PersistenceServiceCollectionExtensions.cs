using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure(options => options.Provider = configuration["Database:Provider"])
            .Validate(DatabaseOptions.IsValid, "Database:Provider must be Sqlite or SqlServer.")
            .ValidateOnStart();

        services.AddDbContext<PoliciesDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("Policies");
            _ = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ProviderKind switch
            {
                DatabaseProvider.Sqlite => options.UseSqlite(connectionString),
                DatabaseProvider.SqlServer => options.UseSqlServer(connectionString),
                _ => throw new InvalidOperationException("Unknown database provider."),
            };
        });

        services.AddScoped<IPolicyRepository, PolicyRepository>();

        services.AddHealthChecks().AddDbContextCheck<PoliciesDbContext>(tags: ["ready"]);

        return services;
    }

    // Migrations are SQL Server only, so with Sqlite the schema is created from the model at start-up.
    public static void EnsureSqliteCreated(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ProviderKind == DatabaseProvider.Sqlite)
        {
            scope.ServiceProvider.GetRequiredService<PoliciesDbContext>().Database.EnsureCreated();
        }
    }
}
