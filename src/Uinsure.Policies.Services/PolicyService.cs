using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public sealed class PolicyService(IPolicyRepository repository, IReferenceGenerator references, TimeProvider clock) : IPolicyService
{
    public async Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);

        // SELL-1: a duplicate is a 409 returned alone, before any 422 rule is evaluated.
        if (await repository.GetByReference(command.Reference, ct) is not null)
        {
            return Result<PolicyDetails>.Failure(DuplicateReference(command.Reference));
        }

        var sale = Policy.Sell(
            command.Reference,
            command.Type,
            command.StartDate,
            command.Amount,
            command.AutoRenew,
            command.Policyholders,
            command.Property,
            command.PaymentMethod,
            references.Payment(),
            today);

        if (!sale.IsSuccess)
        {
            return Result<PolicyDetails>.Failure([.. sale.Violations]);
        }

        var policy = sale.Value;
        repository.Add(policy);
        try
        {
            await repository.SaveChanges(ct);
        }
        catch (PersistenceConflictException conflict) when (conflict.Reason == ConflictReason.DuplicateReference)
        {
            // SELL-1: a sale of the same reference raced past the check above and hit the unique index.
            // It answers exactly as the check does.
            return Result<PolicyDetails>.Failure(DuplicateReference(command.Reference));
        }

        return Result<PolicyDetails>.Success(Details(policy, today));
    }

    public async Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);
        var policy = await repository.GetByReference(reference, ct);
        if (policy is null)
        {
            return Result<PolicyDetails>.Failure(Violation(
                RuleIds.Get1,
                ViolationKind.NotFound,
                $"No policy with reference '{reference.Value}'.",
                reference));
        }

        return Result<PolicyDetails>.Success(Details(policy, today));
    }

    public Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct) => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<RefundCalculation>>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct) => throw new NotImplementedException();

    public Task<Result<Claim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct) => throw new NotImplementedException();

    private static PolicyDetails Details(Policy policy, DateOnly today) =>
        new(policy, policy.CurrentTerm(today), policy.StatusOn(today));

    private static RuleViolation DuplicateReference(PolicyReference reference) => Violation(
        RuleIds.Sell1,
        ViolationKind.Conflict,
        $"A policy with reference '{reference.Value}' already exists.",
        reference);

    private static RuleViolation Violation(string ruleId, ViolationKind kind, string message, PolicyReference reference) =>
        new(ruleId, kind, message, new Dictionary<string, object?> { ["reference"] = reference.Value });
}
