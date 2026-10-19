namespace Uinsure.Policies.Api.Contracts;

// The full PolicyResponse of the API contract (GET-1). PolicyMappings fills it from the stored policy and
// lists there which members are still canned.
public sealed record PolicyResponse(
    string Reference,
    PolicyType Type,
    PolicyStatus Status,
    int CurrentTermNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Amount,
    bool HasClaims,
    bool AutoRenew,
    IReadOnlyList<PolicyholderDto> Policyholders,
    PropertyDto Property,
    IReadOnlyList<TermResponse> Terms,
    IReadOnlyList<PaymentResponse> Payments,
    IReadOnlyList<RefundResponse> Refunds,
    IReadOnlyList<ClaimResponse> Claims,
    CancellationSummary? Cancellation);

public enum PolicyStatus
{
    Pending,
    Active,
    Expired,
    Cancelled,
}

public sealed record TermResponse(int TermNumber, DateOnly StartDate, DateOnly EndDate, decimal Amount, string? PaymentReference);

public sealed record PaymentResponse(string Reference, PaymentMethod Type, decimal Amount);

public sealed record RefundResponse(string Reference, PaymentMethod Type, decimal Amount, string PaymentReference);

public sealed record ClaimResponse(string Reference, DateOnly ClaimDate, string? Description);

public sealed record CancellationSummary(DateOnly CancellationDate, string RuleApplied, decimal TotalRefund);
