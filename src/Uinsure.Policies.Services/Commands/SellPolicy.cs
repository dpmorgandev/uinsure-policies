using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Commands;

public sealed record SellPolicy(PolicyReference Reference, PolicyType Type, DateOnly StartDate, decimal Amount, bool AutoRenew, IReadOnlyList<Policyholder> Policyholders, Property Property, PaymentMethod PaymentMethod);
