namespace Uinsure.Policies.Domain.Rules;

public sealed record RefundCalculation(decimal Amount, string RuleApplied, int TermDays, int UsedDays, int UnusedDays);
