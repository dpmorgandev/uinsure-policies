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

    Task<Result<CancellationQuoteResult>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct);

    Task<Result<RecordedClaim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);

    // Removed once tests move to QuoteCancellation
    Task<Result<CancellationQuoteResult>> Quote(PolicyReference reference, DateOnly? on, CancellationToken ct);

    // Removed once tests move to RecordClaim
    Task<Result<RecordedClaim>> RecordNumberedClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);
}
