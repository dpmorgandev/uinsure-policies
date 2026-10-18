using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Tests.Support;

// The API with the app's own IPolicyRepository wrapped, so a test can make one sale lose the SELL-1 race:
// a reference armed with HideOnceFromDuplicateCheck is hidden from the next GetByReference only, so that
// sale gets past the service's duplicate check and reaches the real unique index on SaveChanges. Add and
// SaveChanges always go to the app's real PolicyRepository, and every other lookup, including the GET
// read-back, sees the stored policy.
public sealed class DuplicateRaceApiFactory : PoliciesApiFactory
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _armed = [];
    private readonly Dictionary<string, int> _hiddenLookups = [];

    public void HideOnceFromDuplicateCheck(string reference)
    {
        lock (_gate)
        {
            _armed.Add(reference);
        }
    }

    // How many lookups of the reference were hidden: 1 proves the sale took the race path.
    public int HiddenLookups(string reference)
    {
        lock (_gate)
        {
            return _hiddenLookups.GetValueOrDefault(reference);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var app = services.Single(descriptor => descriptor.ServiceType == typeof(IPolicyRepository));
            var implementation = app.ImplementationType
                ?? throw new InvalidOperationException("The app's IPolicyRepository is expected to be registered by type.");
            services.Remove(app);
            services.Add(new ServiceDescriptor(
                typeof(IPolicyRepository),
                provider => new RacingPolicyRepository((IPolicyRepository)ActivatorUtilities.CreateInstance(provider, implementation), this),
                app.Lifetime));
        });
    }

    private bool TryHide(PolicyReference reference)
    {
        lock (_gate)
        {
            if (!_armed.Remove(reference.Value))
            {
                return false;
            }

            _hiddenLookups[reference.Value] = _hiddenLookups.GetValueOrDefault(reference.Value) + 1;
            return true;
        }
    }

    private sealed class RacingPolicyRepository(IPolicyRepository real, DuplicateRaceApiFactory factory) : IPolicyRepository
    {
        public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct) =>
            factory.TryHide(reference) ? Task.FromResult<Policy?>(null) : real.GetByReference(reference, ct);

        public void Add(Policy policy) => real.Add(policy);

        public Task SaveChanges(CancellationToken ct) => real.SaveChanges(ct);
    }
}
