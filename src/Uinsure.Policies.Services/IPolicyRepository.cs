using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services;

public interface IPolicyRepository
{
    public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct) => throw new NotImplementedException();

    public void Add(Policy policy) => throw new NotImplementedException();

    public Task SaveChanges(CancellationToken ct) => throw new NotImplementedException();
}
