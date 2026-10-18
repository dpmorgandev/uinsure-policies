using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public interface IPolicyService
{
    Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct);

    Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct);
}
