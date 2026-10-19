using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-1c: from start + 15, after cooling-off, the refund is amount × unused days ÷ the term's actual days
// (365 or 366), with the cancellation day counted as used, rounded once (CANCEL-6). No £0 Refund is created, but
// the term's calculation is still returned. Each refund is a literal worked out by hand:
//   start 2026-11-08 ends 2027-11-07; start 2026-10-17 (today) ends 2027-10-16 (both 365 days);
//   start 2027-03-01 ends 2028-02-29 (366 days, sold on 2027-02-01).
public sealed class CancelProRataTests
{
    private static readonly DateOnly Today = TestClock.Today;
    private static readonly DateOnly Start365 = new(2026, 11, 8);
    private static readonly DateOnly Start366 = new(2027, 3, 1);
    private static readonly DateOnly SaleDayFor366 = new(2027, 2, 1);

    private static Term PaidTerm(DateOnly start, decimal amount) =>
        new(start, amount, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, amount));

    // 2026-11-23 is start + 15: 527.18 × 349 ÷ 365 = 504.0707… → 504.07.
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    [Trait("Rule", "CANCEL-6")]
    public void Calculation_on_start_plus_15_is_the_unused_days()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(Start365, 527.18m), new DateOnly(2026, 11, 23), hasClaims: false);

        Assert.Equal(new RefundCalculation(504.07m, "CANCEL-1c", 365, 16, 349), calculation);
    }

    // 2027-05-08: 182 days used, 183 unused; 730.00 × 183 ÷ 365 = 366.00 (÷ 366 would give 365.00).
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public void Calculation_mid_term_of_a_365_day_term_divides_by_365()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(Start365, 730.00m), new DateOnly(2027, 5, 8), hasClaims: false);

        Assert.Equal(new RefundCalculation(366.00m, "CANCEL-1c", 365, 182, 183), calculation);
    }

    // 2027-03-16 is start + 15: 610.00 × 350 ÷ 366 = 583.333… → 583.33 (÷ 365 would give 584.93).
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public void Calculation_on_start_plus_15_of_a_366_day_term_divides_by_366()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(Start366, 610.00m), new DateOnly(2027, 3, 16), hasClaims: false);

        Assert.Equal(new RefundCalculation(583.33m, "CANCEL-1c", 366, 16, 350), calculation);
    }

    // The end date is the last day of cover, and it is used.
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public void Calculation_on_the_last_day_of_cover_is_nothing()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(Start365, 527.18m), new DateOnly(2027, 11, 7), hasClaims: false);

        Assert.Equal(new RefundCalculation(0m, "CANCEL-1c", 365, 365, 0), calculation);
    }

    // 2026-11-01 is start + 15 of a policy starting today: 410.00 × 349 ÷ 365 = 392.0273… → 392.03.
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    [Trait("Rule", "CANCEL-6")]
    [Trait("Rule", "CANCEL-2")]
    public void Cancel_on_start_plus_15_refunds_the_unused_days()
    {
        var policy = new PolicyBuilder()
            .WithStartDate(Today).WithAmount(410.00m).WithPaymentMethod(PaymentMethod.Card).WithPaymentReference("PAY-DOM-1C15")
            .Build();

        var result = policy.Cancel(new DateOnly(2026, 11, 1), Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2026, 11, 1), "CANCEL-1c", 392.03m,
            [new RefundCalculation(392.03m, "CANCEL-1c", 365, 16, 349)],
            [new Refund("REF-DOM-0001", PaymentMethod.Card, 392.03m, "PAY-DOM-1C15")]);
    }

    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public void Cancel_on_start_plus_15_of_a_366_day_term_refunds_over_366_days()
    {
        var policy = new PolicyBuilder()
            .WithToday(SaleDayFor366).WithStartDate(Start366).WithAmount(610.00m)
            .Build();

        var result = policy.Cancel(new DateOnly(2027, 3, 16), SaleDayFor366, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2027, 3, 16), "CANCEL-1c", 583.33m,
            [new RefundCalculation(583.33m, "CANCEL-1c", 366, 16, 350)],
            [new Refund("REF-DOM-0001", PaymentMethod.DirectDebit, 583.33m, "PAY-DOM-0042")]);
    }

    // No unused day: the term is listed with 0, and no £0 Refund is created.
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public void Cancel_on_the_last_day_of_cover_lists_the_term_and_creates_no_refund()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(new DateOnly(2027, 11, 7), Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2027, 11, 7), "CANCEL-1c", 0m,
            [new RefundCalculation(0m, "CANCEL-1c", 365, 365, 0)],
            []);
    }
}
