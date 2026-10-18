using Uinsure.Policies.Domain.Common;

namespace Uinsure.Policies.Domain.Policies;

public sealed class Policy
{
    public required IReadOnlyList<Policyholder> Policyholders { get; init; }
    public required IReadOnlyList<Term> Terms { get; init; }
    public static Result<Policy> Sell(PolicyReference reference, PolicyType type, DateOnly start, decimal amount, bool autoRenew, IReadOnlyList<Policyholder> holders, Property property, PaymentMethod method, string paymentReference, DateOnly today) => throw new NotImplementedException();
}
