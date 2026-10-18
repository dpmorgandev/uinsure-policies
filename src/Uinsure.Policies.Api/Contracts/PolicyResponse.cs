namespace Uinsure.Policies.Api.Contracts;

// F1 carries what a sale reads back: the current term's dates and amount, holders, property, terms and
// payments. F2 adds only status, currentTermNumber, hasClaims, refunds, claims and cancellation.
public sealed record PolicyResponse(
    string Reference,
    PolicyType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Amount,
    bool AutoRenew,
    IReadOnlyList<PolicyholderDto> Policyholders,
    PropertyDto Property,
    IReadOnlyList<TermResponse> Terms,
    IReadOnlyList<PaymentResponse> Payments);

public sealed record TermResponse(int TermNumber, DateOnly StartDate, DateOnly EndDate, decimal Amount, string? PaymentReference);

public sealed record PaymentResponse(string Reference, PaymentMethod Type, decimal Amount);
