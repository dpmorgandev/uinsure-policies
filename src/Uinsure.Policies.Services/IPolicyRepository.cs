using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services;

public interface IPolicyRepository
{
    Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct);

    void Add(Policy policy);

    Task SaveChanges(CancellationToken ct);
}
