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
            return Result<PolicyDetails>.Failure(NotFound(reference));
        }

        return Result<PolicyDetails>.Success(Details(policy, today));
    }

    public async Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);
        var policy = await repository.GetByReference(reference, ct);
        if (policy is null)
        {
            return Result<Cancellation>.Failure(NotFound(reference));
        }

        var cancellation = policy.Cancel(on, today, references.Refund);
        if (cancellation.IsSuccess)
        {
            await repository.SaveChanges(ct);
        }

        return cancellation;
    }

    public async Task<Result<CancellationQuoteResult>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);
        var policy = await repository.GetByReference(reference, ct);
        if (policy is null)
        {
            return Result<CancellationQuoteResult>.Failure(NotFound(reference));
        }

        // COULD-1: a quote is read-only, so it never saves.
        var quote = policy.QuoteCancellation(on, today);
        return quote.IsSuccess
            ? Result<CancellationQuoteResult>.Success(new CancellationQuoteResult(quote.Value, today))
            : Result<CancellationQuoteResult>.Failure([.. quote.Violations]);
    }

    public async Task<Result<RecordedClaim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);
        var policy = await repository.GetByReference(reference, ct);
        if (policy is null)
        {
            return Result<RecordedClaim>.Failure(NotFound(reference));
        }

        var claim = policy.RecordClaim(claimDate, description, today);
        if (!claim.IsSuccess)
        {
            return Result<RecordedClaim>.Failure([.. claim.Violations]);
        }

        // The new claim is the last in the policy's list; its number is taken now, so a later claim cannot change it.
        var recorded = new RecordedClaim(claim.Value, policy.Claims.Count, policy.HasClaims);
        await repository.SaveChanges(ct);
        return Result<RecordedClaim>.Success(recorded);
    }

    private static RuleViolation NotFound(PolicyReference reference) => Violation(
        RuleIds.Get1,
        ViolationKind.NotFound,
        $"No policy with reference '{reference.Value}'.",
        reference);

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
