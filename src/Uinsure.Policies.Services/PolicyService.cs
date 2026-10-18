using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public sealed class PolicyService(IPolicyRepository repository, TimeProvider clock)
{
    private readonly IPolicyRepository _repository = repository;
    private readonly TimeProvider _clock = clock;

    public Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct) => throw new NotImplementedException();
}
