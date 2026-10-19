using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Domain.Rules;

public static class RefundCalculator
{
    private const int CoolingOffDays = 14;
    private const string UnpaidTerm = "UNPAID-TERM";

    // The one calculation behind a cancellation and its quote. Money.Round is called once, on the final amount.
    public static RefundCalculation ForTerm(Term term, DateOnly cancelOn, bool hasClaims)
    {
        var termDays = term.Days;
        var usedDays = Math.Clamp(cancelOn.DayNumber - term.Start.DayNumber + 1, 0, termDays);
        var unusedDays = termDays - usedDays;

        var (ruleApplied, unrounded) = Decide(term, cancelOn, hasClaims, termDays, unusedDays);
        return new RefundCalculation(Money.Round(unrounded), ruleApplied, termDays, usedDays, unusedDays);
    }

    private static (string RuleApplied, decimal Amount) Decide(Term term, DateOnly cancelOn, bool hasClaims, int termDays, int unusedDays)
    {
        // No payment, nothing to reverse: this wins over a claim.
        if (term.Payment is null)
        {
            return (UnpaidTerm, 0m);
        }

        if (hasClaims)
        {
            return (RuleIds.Could2, 0m);
        }

        if (cancelOn < term.Start)
        {
            return (RuleIds.Cancel1a, term.Amount);
        }

        // CANCEL-1b: cooling-off runs to start + 14 days, inclusive.
        if (cancelOn <= term.Start.AddDays(CoolingOffDays))
        {
            return (RuleIds.Cancel1b, term.Amount);
        }

        // CANCEL-1c: the cancellation day counts as used; divide by the term's actual days (365 or 366).
        return (RuleIds.Cancel1c, term.Amount * unusedDays / termDays);
    }
}
