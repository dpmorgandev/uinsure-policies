using Riok.Mapperly.Abstractions;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Api.Contracts;

// Compile-time mapping between the contract DTOs and the domain. The enums and the domain-to-DTO copies are
// generated; the request side is written by hand because [Required] has already made its nullable members
// non-null by the time these run.
[Mapper]
internal static partial class PolicyMappings
{
    public static SellPolicy ToCommand(this SellPolicyRequest request) => new(
        PolicyReference.Normalise(request.Reference!),
        ToDomain(request.Type!.Value),
        request.StartDate!.Value,
        request.Amount!.Value,
        request.AutoRenew!.Value,
        [.. request.Policyholders!.Select(holder => new Policyholder(holder.FirstName!, holder.LastName!, holder.DateOfBirth!.Value))],
        new Property(request.Property!.AddressLine1!, request.Property.AddressLine2, request.Property.AddressLine3, request.Property.Postcode!),
        ToDomain(request.PaymentMethod!.Value));

    // Still canned until a later node supplies the data: refunds, claims and cancellation (empty, empty, null).
    // The policy-level dates and amount are the current term's (GET-1).
    public static PolicyResponse ToResponse(this PolicyDetails details)
    {
        var policy = details.Policy;
        var term = details.CurrentTerm;
        return new PolicyResponse(
            policy.Reference.Value,
            ToDto(policy.Type),
            ToDto(details.Status),
            policy.Terms.ToList().IndexOf(term) + 1,
            term.Start,
            term.End,
            term.Amount,
            policy.HasClaims,
            policy.AutoRenew,
            [.. policy.Policyholders.Select(ToDto)],
            ToDto(policy.Property),
            [.. policy.Terms.Select((t, index) => new TermResponse(index + 1, t.Start, t.End, t.Amount, t.Payment?.Reference))],
            [.. policy.Terms.Where(t => t.Payment is not null).Select(t => new PaymentResponse(t.Payment!.Reference, ToDto(t.Payment.Method), t.Payment.Amount))],
            [],
            [],
            null);
    }

    private static partial Domain.Policies.PolicyType ToDomain(PolicyType type);

    private static partial Domain.Policies.PaymentMethod ToDomain(PaymentMethod method);

    private static partial PolicyType ToDto(Domain.Policies.PolicyType type);

    private static partial PolicyStatus ToDto(Domain.Policies.PolicyStatus status);

    private static partial PaymentMethod ToDto(Domain.Policies.PaymentMethod method);

    private static partial PolicyholderDto ToDto(Policyholder holder);

    private static partial PropertyDto ToDto(Property property);
}
