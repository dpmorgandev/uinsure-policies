using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Tests.Support;

// A repository on which another write wins the race. GetByReference finds nothing, because the other sale
// has not committed when the service checks, and SaveChanges then fails with the given conflict, as
// PolicyRepository does when the unique index on Reference (DuplicateReference) or the Version token
// (Concurrency) rejects the write. Every call is counted so a test can see the check ran before the save.
internal sealed class ConflictOnSavePolicyRepository(ConflictReason reason) : IPolicyRepository
{
    public int GetByReferenceCalls { get; private set; }

    public int AddCalls { get; private set; }

    public int SaveChangesCalls { get; private set; }

    public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
    {
        GetByReferenceCalls++;
        return Task.FromResult<Policy?>(null);
    }

    public void Add(Policy policy) => AddCalls++;

    public Task SaveChanges(CancellationToken ct)
    {
        SaveChangesCalls++;
        throw new PersistenceConflictException(reason);
    }
}
