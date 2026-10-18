using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public sealed class PolicyService(IPolicyRepository repository, TimeProvider clock)
{
    private readonly IPolicyRepository _repository = repository;
    private readonly TimeProvider _clock = clock;

    public async Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct)
    {
        var today = UkCalendar.Today(_clock);

        var sale = Policy.Sell(
            command.Reference,
            command.Type,
            command.StartDate,
            command.Amount,
            command.AutoRenew,
            command.Policyholders,
            command.Property,
            command.PaymentMethod,
            ReferenceGenerator.Payment(),
            today);

        if (!sale.IsSuccess)
        {
            return Result<PolicyDetails>.Failure([.. sale.Violations]);
        }

        var policy = sale.Value;
        _repository.Add(policy);
        await _repository.SaveChanges(ct);

        // A new sale has one term and no cancellation, so it is Pending until its start date, then Active.
        var term = policy.Terms[0];
        var status = today < term.Start ? PolicyStatus.Pending : PolicyStatus.Active;
        return Result<PolicyDetails>.Success(new PolicyDetails(policy, term, status));
    }
}
