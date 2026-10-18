using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;

namespace Uinsure.Policies.Api.Contracts;

// Plain C# mapping between the contract DTOs and the domain. [Required] has already made the request's
// members non-null by the time these run.
internal static class PolicyMappings
{
    public static SellPolicy ToCommand(this SellPolicyRequest request) => new(
        PolicyReference.Normalise(request.Reference!),
        Enum.Parse<Domain.Policies.PolicyType>(request.Type!.Value.ToString()),
        request.StartDate!.Value,
        request.Amount!.Value,
        request.AutoRenew!.Value,
        [.. request.Policyholders!.Select(holder => new Policyholder(holder.FirstName!, holder.LastName!, holder.DateOfBirth!.Value))],
        new Property(request.Property!.AddressLine1!, request.Property.AddressLine2, request.Property.AddressLine3, request.Property.Postcode!),
        Enum.Parse<Domain.Policies.PaymentMethod>(request.PaymentMethod!.Value.ToString()));

    // The policy-level dates and amount are the current term's (GET-1).
    public static PolicyResponse ToResponse(this PolicyDetails details)
    {
        var policy = details.Policy;
        var term = details.CurrentTerm;
        return new PolicyResponse(
            policy.Reference.Value,
            Enum.Parse<PolicyType>(policy.Type.ToString()),
            term.Start,
            term.End,
            term.Amount,
            policy.AutoRenew,
            [.. policy.Policyholders.Select(holder => new PolicyholderDto { FirstName = holder.FirstName, LastName = holder.LastName, DateOfBirth = holder.DateOfBirth })],
            new PropertyDto
            {
                AddressLine1 = policy.Property.AddressLine1,
                AddressLine2 = policy.Property.AddressLine2,
                AddressLine3 = policy.Property.AddressLine3,
                Postcode = policy.Property.Postcode,
            },
            [.. policy.Terms.Select((t, index) => new TermResponse(index + 1, t.Start, t.End, t.Amount, t.Payment?.Reference))],
            [.. policy.Terms.Where(t => t.Payment is not null).Select(t => new PaymentResponse(t.Payment!.Reference, Enum.Parse<PaymentMethod>(t.Payment.Method.ToString()), t.Payment.Amount))]);
    }
}
