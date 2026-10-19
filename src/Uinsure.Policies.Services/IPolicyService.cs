using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public interface IPolicyService
{
    Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct);

    Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct);

    Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct);

    Task<Result<CancellationQuoteResult>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct);

    Task<Result<Term>> Renew(PolicyReference reference, decimal amount, PaymentMethod? method, CancellationToken ct);

    Task<Result<RecordedClaim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);
}
