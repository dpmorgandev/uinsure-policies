using System.ComponentModel.DataAnnotations;

namespace Uinsure.Policies.Api.Contracts;

// E3 body. Optional: no body, {} or a null date all mean today (CANCEL-3).
public sealed record CancelPolicyRequest(DateOnly? CancellationDate);

public sealed record CancellationTermResponse(
    int TermNumber,
    string RuleApplied,
    int DaysInTerm,
    int DaysUsed,
    int UnusedDays,
    decimal RefundAmount);

public sealed record CancellationRefundResponse(string Reference, PaymentMethod Type, decimal Amount, string PaymentReference);

public sealed record CancellationResponse(
    string Reference,
    DateOnly CancellationDate,
    string RuleApplied,
    decimal TotalRefund,
    IReadOnlyList<CancellationTermResponse> Terms,
    IReadOnlyList<CancellationRefundResponse> Refunds);

// A quote creates nothing, so its refunds carry no reference.
public sealed record QuoteRefundResponse(PaymentMethod Type, decimal Amount, string PaymentReference);

public sealed record CancellationQuoteResponse(
    string Reference,
    DateOnly CancellationDate,
    string RuleApplied,
    decimal TotalRefund,
    IReadOnlyList<CancellationTermResponse> Terms,
    IReadOnlyList<QuoteRefundResponse> Refunds,
    DateOnly QuotedOn);

public sealed record RecordClaimRequest(
    [property: Required] DateOnly? ClaimDate,
    [property: MaxLength(500)] string? Description);

// E5 body. The claim nested in PolicyResponse is ClaimResponse.
public sealed record RecordClaimResponse(
    string Reference,
    string PolicyReference,
    DateOnly ClaimDate,
    string? Description,
    bool HasClaims);
