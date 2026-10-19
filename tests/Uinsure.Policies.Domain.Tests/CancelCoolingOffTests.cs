using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-1b: from the start date to start + 14 inclusive (the 14-day cooling-off, counted from the day after the
// start) the refund is the full amount. The start date is day 1 used. The other sides of the window are
// CancelBeforeStartTests (start − 1) and CancelProRataTests (start + 15).
//   start 2026-11-08 ends 2027-11-07; start 2026-10-17 (today) ends 2027-10-16 (both 365 days).
public sealed class CancelCoolingOffTests
{
    private static readonly DateOnly Today = TestClock.Today;
    private static readonly DateOnly Start = new(2026, 11, 8);

    private static Term Term() => new(Start, 527.18m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m));

    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    public void Calculation_on_the_start_date_is_the_full_amount_with_the_start_day_used()
    {
        var calculation = RefundCalculator.ForTerm(Term(), Start, hasClaims: false);

        Assert.Equal(new RefundCalculation(527.18m, "CANCEL-1b", 365, 1, 364), calculation);
    }

    // 2026-11-22 is start + 14, the last day of cooling-off.
    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    public void Calculation_on_start_plus_14_is_the_full_amount()
    {
        var calculation = RefundCalculator.ForTerm(Term(), new DateOnly(2026, 11, 22), hasClaims: false);

        Assert.Equal(new RefundCalculation(527.18m, "CANCEL-1b", 365, 15, 350), calculation);
    }

    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    [Trait("Rule", "CANCEL-2")]
    public void Cancel_on_the_start_date_refunds_in_full()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(Start, Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, Start, "CANCEL-1b", 527.18m,
            [new RefundCalculation(527.18m, "CANCEL-1b", 365, 1, 364)],
            [new Refund("REF-DOM-0001", PaymentMethod.DirectDebit, 527.18m, "PAY-DOM-0042")]);
    }

    // 2026-10-31 is start + 14 of a policy starting today.
    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    [Trait("Rule", "CANCEL-2")]
    public void Cancel_on_start_plus_14_refunds_in_full()
    {
        var policy = new PolicyBuilder()
            .WithStartDate(Today).WithAmount(318.62m).WithPaymentMethod(PaymentMethod.Cheque).WithPaymentReference("PAY-DOM-1B14")
            .Build();

        var result = policy.Cancel(new DateOnly(2026, 10, 31), Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2026, 10, 31), "CANCEL-1b", 318.62m,
            [new RefundCalculation(318.62m, "CANCEL-1b", 365, 15, 350)],
            [new Refund("REF-DOM-0001", PaymentMethod.Cheque, 318.62m, "PAY-DOM-1B14")]);
    }
}
