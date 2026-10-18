using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Persistence;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Tests.Support;

// The API plus a test-only path that makes a stale save of a sold policy, inside the app's own exception
// handling exactly as a write endpoint would. F1 has no endpoint that changes a stored policy, so the path does
// what a lost race does: it loads the policy through the app's IPolicyRepository, another request then loads
// and saves the same policy with a new Version, and this request saves the copy it loaded. The repository's
// PersistenceConflictException(Concurrency) is then answered by the app. The entry found is the tracked one
// whose Version is EF's concurrency token, whatever its type; the factory records that type's name.
public sealed class StaleSaveApiFactory : PoliciesApiFactory
{
    public const string Path = "/test/stale-save";

    // The Version the other request saves: a literal, never Guid.NewGuid().
    private static readonly Guid OtherRequestVersion = new("0f1b0000-0000-4000-8000-000000000001");

    private readonly Lock _gate = new();
    private string? _concurrencyTokenOwner;

    // The full name of the tracked type whose Version is the concurrency token, seen on the last stale save.
    public string? ConcurrencyTokenOwner
    {
        get
        {
            lock (_gate)
            {
                return _concurrencyTokenOwner;
            }
        }
    }

    public static string PathFor(string reference) => $"{Path}?reference={Uri.EscapeDataString(reference)}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddTransient<IStartupFilter>(_ => new StaleSaveEndpointStartupFilter(this)));
    }

    private static EntityEntry VersionedEntry(PoliciesDbContext context) =>
        context.ChangeTracker.Entries().Single(entry => entry.Metadata.FindProperty("Version")?.IsConcurrencyToken == true);

    private async Task StaleSave(HttpContext context)
    {
        var ct = context.RequestAborted;
        var reference = new PolicyReference(context.Request.Query["reference"].ToString());
        var repository = context.RequestServices.GetRequiredService<IPolicyRepository>();
        if (await repository.GetByReference(reference, ct) is null)
        {
            throw new InvalidOperationException($"'{reference.Value}' was expected to be sold before the stale save.");
        }

        var mine = VersionedEntry(context.RequestServices.GetRequiredService<PoliciesDbContext>());
        lock (_gate)
        {
            _concurrencyTokenOwner = mine.Metadata.ClrType.FullName;
        }

        // Another request loads the same policy and saves it first, with a new Version.
        using (var other = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var otherContext = other.ServiceProvider.GetRequiredService<PoliciesDbContext>();
            _ = await other.ServiceProvider.GetRequiredService<IPolicyRepository>().GetByReference(reference, ct);
            VersionedEntry(otherContext).Property("Version").CurrentValue = OtherRequestVersion;
            await otherContext.SaveChangesAsync(ct);
        }

        // This request saves the copy it loaded, which still carries the Version it was loaded with.
        mine.State = EntityState.Modified;
        await repository.SaveChanges(ct);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private sealed class StaleSaveEndpointStartupFilter(StaleSaveApiFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Path, branch => branch.Run(factory.StaleSave));
        };
    }
}
