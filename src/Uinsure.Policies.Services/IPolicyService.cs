using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public interface IPolicyService
{
    Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct);

    Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct);

    Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct);

    Task<Result<CancellationQuoteResult>> Quote(PolicyReference reference, DateOnly? on, CancellationToken ct);

    Task<Result<RecordedClaim>> RecordNumberedClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);
}
