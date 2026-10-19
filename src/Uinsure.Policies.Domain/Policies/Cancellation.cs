using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Domain.Policies;

public sealed record Cancellation(DateOnly CancellationDate, string RuleApplied, decimal TotalRefund, IReadOnlyList<RefundCalculation> Terms, IReadOnlyList<Refund> Refunds);
