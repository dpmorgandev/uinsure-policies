using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Persistence;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Tests.Support;

// The API with a lost race on demand, through the real F3 endpoints. A test arms a reference
// (LoseTheNextSaveOf); the next request that loaded that policy and then saves it finds that another request
// saved the same policy first, between its load and its save. The app's IPolicyRepository is wrapped so the
// other request runs at exactly that point: it loads the policy in a scope of its own and saves it with a new
// Version (a literal, never Guid.NewGuid()). The armed request's save then still carries the Version it
// loaded, so the repository raises PersistenceConflictException(Concurrency) and the app answers 409.
public sealed class ConcurrentWriteApiFactory : PoliciesApiFactory
{
    private static readonly Guid OtherRequestVersion = new("0f3c0000-0000-4000-8000-000000000003");

    private readonly ConcurrentDictionary<string, bool> _armed = new(StringComparer.Ordinal);

    public void LoseTheNextSaveOf(string reference) => _armed[reference] = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var registered = services.Single(descriptor => descriptor.ServiceType == typeof(IPolicyRepository));
            services.Remove(registered);
            services.AddScoped<IPolicyRepository>(provider => new RacedRepository(AppRepository(provider, registered), provider, this));
        });
    }

    private static IPolicyRepository AppRepository(IServiceProvider provider, ServiceDescriptor registered) =>
        registered.ImplementationFactory is not null
            ? (IPolicyRepository)registered.ImplementationFactory(provider)
            : (IPolicyRepository)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!);

    private bool Disarm(PolicyReference reference) => _armed.TryRemove(reference.Value, out _);

    // The other request: load the same policy and save it with a new Version, before the armed request saves.
    private static async Task SaveFromAnotherRequest(IServiceProvider provider, PolicyReference reference, CancellationToken ct)
    {
        using var other = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var context = other.ServiceProvider.GetRequiredService<PoliciesDbContext>();
        _ = await other.ServiceProvider.GetRequiredService<IPolicyRepository>().GetByReference(reference, ct);
        VersionedEntry(context).Property("Version").CurrentValue = OtherRequestVersion;
        await context.SaveChangesAsync(ct);
    }

    private static EntityEntry VersionedEntry(PoliciesDbContext context) =>
        context.ChangeTracker.Entries().Single(entry => entry.Metadata.FindProperty("Version")?.IsConcurrencyToken == true);

    private sealed class RacedRepository(IPolicyRepository app, IServiceProvider provider, ConcurrentWriteApiFactory factory) : IPolicyRepository
    {
        private readonly List<PolicyReference> _loaded = [];

        public async Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
        {
            var policy = await app.GetByReference(reference, ct);
            if (policy is not null)
            {
                _loaded.Add(reference);
            }

            return policy;
        }

        public void Add(Policy policy) => app.Add(policy);

        public async Task SaveChanges(CancellationToken ct)
        {
            foreach (var reference in _loaded.ToArray())
            {
                if (factory.Disarm(reference))
                {
                    await SaveFromAnotherRequest(provider, reference, ct);
                }
            }

            await app.SaveChanges(ct);
        }
    }
}
