using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Tests.Support;

// A hand-written in-memory repository. A policy is found by GetByReference only after SaveChanges, as with
// the database, and every Add and SaveChanges call is counted so a test can see what the service stored.
internal sealed class RecordingPolicyRepository : IPolicyRepository
{
    private readonly List<Policy> _pending = [];
    private readonly List<Policy> _saved = [];

    public IReadOnlyList<Policy> Saved => _saved;

    public int AddCalls { get; private set; }

    public int SaveChangesCalls { get; private set; }

    public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct) =>
        Task.FromResult(_saved.FirstOrDefault(policy => policy.Reference == reference));

    public void Add(Policy policy)
    {
        AddCalls++;
        _pending.Add(policy);
    }

    public Task SaveChanges(CancellationToken ct)
    {
        SaveChangesCalls++;
        _saved.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }
}
