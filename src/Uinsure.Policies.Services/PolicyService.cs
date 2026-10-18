using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Services;

public sealed class PolicyService(IPolicyRepository repository, TimeProvider clock)
{
    public async Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct)
    {
        var today = UkCalendar.Today(clock);

        // SELL-1: a duplicate is a 409 returned alone, before any 422 rule is evaluated.
        if (await repository.GetByReference(command.Reference, ct) is not null)
        {
            return Result<PolicyDetails>.Failure(Violation(
                RuleIds.Sell1,
                ViolationKind.Conflict,
                $"A policy with reference '{command.Reference.Value}' already exists.",
                command.Reference));
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
            ReferenceGenerator.Payment(),
            today);

        if (!sale.IsSuccess)
        {
            return Result<PolicyDetails>.Failure([.. sale.Violations]);
        }

        var policy = sale.Value;
        repository.Add(policy);
        await repository.SaveChanges(ct);

        return Result<PolicyDetails>.Success(Details(policy));
    }

    public async Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct)
    {
        var policy = await repository.GetByReference(reference, ct);
        if (policy is null)
        {
            return Result<PolicyDetails>.Failure(Violation(
                RuleIds.Get1,
                ViolationKind.NotFound,
                $"No policy with reference '{reference.Value}'.",
                reference));
        }

        return Result<PolicyDetails>.Success(Details(policy));
    }

    // F1 sells one term and nothing changes it, so the policy's only term is its current term.
    // Status and the current term over several terms arrive with GET-1 in F2.
    private static PolicyDetails Details(Policy policy) => new(policy, policy.Terms[0]);

    private static RuleViolation Violation(string ruleId, ViolationKind kind, string message, PolicyReference reference) =>
        new(ruleId, kind, message, new Dictionary<string, object?> { ["reference"] = reference.Value });
}
