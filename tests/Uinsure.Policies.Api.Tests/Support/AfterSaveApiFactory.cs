using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Tests.Support;

// The API with something happening to a policy straight after a request has saved it, through the real F3
// endpoints. The app's IPolicyRepository is wrapped. A test arms a reference, and the next request that loaded
// that policy and then saved it meets, once its own save has committed, one of:
//   AnotherClaimAfterTheNextSaveOf: another request records a claim on the same policy, through the app's own
//     IPolicyService in a scope of its own, as a concurrent claim committing just after this one would.
//   NothingFoundAfterTheNextSaveOf: any later load of that policy in the same request finds nothing, as a read
//     that fails would.
// Each arming is used once, by the first request that saves the policy after it.
public sealed class AfterSaveApiFactory : PoliciesApiFactory
{
    private readonly ConcurrentDictionary<string, OtherClaim> _otherClaims = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unreadable = new(StringComparer.Ordinal);

    public void AnotherClaimAfterTheNextSaveOf(string reference, DateOnly claimDate, string description) =>
        _otherClaims[reference] = new OtherClaim(claimDate, description);

    public void NothingFoundAfterTheNextSaveOf(string reference) => _unreadable[reference] = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var registered = services.Single(descriptor => descriptor.ServiceType == typeof(IPolicyRepository));
            services.Remove(registered);
            services.AddScoped<IPolicyRepository>(provider => new AfterSaveRepository(AppRepository(provider, registered), provider, this));
        });
    }

    private static IPolicyRepository AppRepository(IServiceProvider provider, ServiceDescriptor registered) =>
        registered.ImplementationFactory is not null
            ? (IPolicyRepository)registered.ImplementationFactory(provider)
            : (IPolicyRepository)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!);

    // The other request: record its claim on the same policy, in a scope of its own, after this one's save.
    private static async Task RecordFromAnotherRequest(IServiceProvider provider, PolicyReference reference, OtherClaim claim, CancellationToken ct)
    {
        using var other = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var recorded = await other.ServiceProvider.GetRequiredService<IPolicyService>().RecordNumberedClaim(reference, claim.ClaimDate, claim.Description, ct);
        if (!recorded.IsSuccess)
        {
            throw new InvalidOperationException($"The other request's claim on '{reference.Value}' was expected to be recorded.");
        }
    }

    private sealed record OtherClaim(DateOnly ClaimDate, string Description);

    private sealed class AfterSaveRepository(IPolicyRepository app, IServiceProvider provider, AfterSaveApiFactory factory) : IPolicyRepository
    {
        private readonly List<PolicyReference> _loaded = [];
        private readonly HashSet<PolicyReference> _unreadable = [];

        public async Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
        {
            if (_unreadable.Contains(reference))
            {
                return null;
            }

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
            await app.SaveChanges(ct);

            foreach (var reference in _loaded.Distinct().ToArray())
            {
                if (factory._otherClaims.TryRemove(reference.Value, out var claim))
                {
                    await RecordFromAnotherRequest(provider, reference, claim, ct);
                }

                if (factory._unreadable.TryRemove(reference.Value, out _))
                {
                    _unreadable.Add(reference);
                }
            }
        }
    }
}
