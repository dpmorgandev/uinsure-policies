using Riok.Mapperly.Abstractions;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
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
            [.. (policy.Cancellation?.Refunds ?? []).Select(refund => new RefundResponse(refund.Reference, ToDto(refund.Method), refund.Amount, refund.PaymentReference))],
            [.. policy.Claims.Select((claim, index) => new ClaimResponse(ClaimReference(policy.Reference, index + 1), claim.ClaimDate, claim.Description))],
            policy.Cancellation is { } cancellation
                ? new CancellationSummary(cancellation.CancellationDate, cancellation.RuleApplied, cancellation.TotalRefund)
                : null);
    }

    public static CancellationResponse ToResponse(this Cancellation cancellation, PolicyReference reference) => new(
        reference.Value,
        cancellation.CancellationDate,
        cancellation.RuleApplied,
        cancellation.TotalRefund,
        ToTerms(cancellation.Terms),
        [.. cancellation.Refunds.Select(refund => new CancellationRefundResponse(refund.Reference, ToDto(refund.Method), refund.Amount, refund.PaymentReference))]);

    // The decision (rule, total, refunds) is the domain's; this only maps it.
    public static CancellationQuoteResponse ToResponse(this CancellationQuoteResult result, PolicyReference reference) => new(
        reference.Value,
        result.Quote.CancellationDate,
        result.Quote.RuleApplied,
        result.Quote.TotalRefund,
        ToTerms(result.Quote.Terms),
        [.. result.Quote.Refunds.Select(refund => new QuoteRefundResponse(ToDto(refund.Method), refund.Amount, refund.PaymentReference))],
        result.QuotedOn);

    // A claim has no stored reference of its own: it is the policy's reference and the claim's place in its list.
    // HasClaims is the policy's own flag, read after the claim was recorded (COULD-2).
    public static RecordClaimResponse ToResponse(this RecordedClaim recorded, PolicyReference reference) => new(
        ClaimReference(reference, recorded.Number),
        reference.Value,
        recorded.Claim.ClaimDate,
        recorded.Claim.Description,
        recorded.HasClaims);

    private static string ClaimReference(PolicyReference policy, int claimNumber) => $"CLM-{policy.Value}-{claimNumber}";

    private static List<CancellationTermResponse> ToTerms(IReadOnlyList<RefundCalculation> terms) =>
        [.. terms.Select((term, index) => new CancellationTermResponse(index + 1, term.RuleApplied, term.TermDays, term.UsedDays, term.UnusedDays, term.Amount))];

    private static partial Domain.Policies.PolicyType ToDomain(PolicyType type);

    public static partial Domain.Policies.PaymentMethod ToDomain(this PaymentMethod method);

    private static partial PolicyType ToDto(Domain.Policies.PolicyType type);

    private static partial PolicyStatus ToDto(Domain.Policies.PolicyStatus status);

    private static partial PaymentMethod ToDto(Domain.Policies.PaymentMethod method);

    private static partial PolicyholderDto ToDto(Policyholder holder);

    private static partial PropertyDto ToDto(Property property);
}
