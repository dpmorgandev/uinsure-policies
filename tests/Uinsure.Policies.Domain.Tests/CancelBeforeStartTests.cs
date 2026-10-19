using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-1a: cancelled before the start date, the refund is the full amount. RefundCalculator.ForTerm is the
// one calculation behind a cancellation and its quote; policy.Cancel returns every term's calculation and a
// Refund for each amount above 0, against the payment it reverses. Day counts are the calendar facts whichever
// rule decides: TermDays = end − start + 1; UsedDays = the days from the start up to and including the
// cancellation date, 0 before the start; UnusedDays = TermDays − UsedDays.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, ending 2027-11-07 (365 days), 527.18 by
//   DirectDebit with payment PAY-DOM-0042. The boundary's other side, the start date, is CancelCoolingOffTests'.
public sealed class CancelBeforeStartTests
{
    private static readonly DateOnly Today = TestClock.Today;
    private static readonly DateOnly DayBeforeStart = new(2026, 11, 7);

    [Fact]
    [Trait("Rule", "CANCEL-1a")]
    public void Calculation_on_the_day_before_the_start_is_the_full_amount()
    {
        var term = new Term(new DateOnly(2026, 11, 8), 527.18m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m));

        var calculation = RefundCalculator.ForTerm(term, DayBeforeStart, hasClaims: false);

        Assert.Equal(new RefundCalculation(527.18m, "CANCEL-1a", 365, 0, 365), calculation);
    }

    [Fact]
    [Trait("Rule", "CANCEL-1a")]
    [Trait("Rule", "CANCEL-2")]
    public void Cancel_on_the_day_before_the_start_refunds_in_full_to_the_payment()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(DayBeforeStart, Today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value, DayBeforeStart, "CANCEL-1a", 527.18m,
            [new RefundCalculation(527.18m, "CANCEL-1a", 365, 0, 365)],
            [new Refund("REF-DOM-0001", PaymentMethod.DirectDebit, 527.18m, "PAY-DOM-0042")]);
    }

    // The aggregate holds the cancellation it returned, for the status, a second cancel and persistence to read.
    // Plumbing for CANCEL-4 and the status rather than a rule of its own, so it carries no trait.
    [Fact]
    public void Cancelled_policy_keeps_the_cancellation()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(DayBeforeStart, Today, new RefundReferences().Next);

        Assert.Same(result.Value, policy.Cancellation);
    }
}
