using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Domain.Policies;

// COULD-1: what a cancellation on CancellationDate would decide. Cancel turns the same decision into a Cancellation.
public sealed record CancellationQuote(
    DateOnly CancellationDate,
    string RuleApplied,
    decimal TotalRefund,
    IReadOnlyList<RefundCalculation> Terms,
    IReadOnlyList<QuotedRefund> Refunds);

public sealed record QuotedRefund(PaymentMethod Method, decimal Amount, string PaymentReference);
