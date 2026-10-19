using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Tests.Support;

// A repository that already holds one policy, as the database does after a sale. GetByReference hands back
// that same instance for its reference (and nothing for any other), so a test sees what the service did to it.
// Every call is counted, so a test can see whether the service saved.
internal sealed class StoredPolicyRepository(Policy stored) : IPolicyRepository
{
    public int GetByReferenceCalls { get; private set; }

    public int AddCalls { get; private set; }

    public int SaveChangesCalls { get; private set; }

    public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
    {
        GetByReferenceCalls++;
        return Task.FromResult(stored.Reference == reference ? stored : null);
    }

    public void Add(Policy policy) => AddCalls++;

    public Task SaveChanges(CancellationToken ct)
    {
        SaveChangesCalls++;
        return Task.CompletedTask;
    }
}
