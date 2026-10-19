using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-6: a refund is rounded once, to 2 dp, half away from zero (Money.Round; MoneyTests). Only a 366-day term
// can put a pro-rata refund exactly on half a penny (amount × unused ÷ 365 never ends in .xx5), so the term starts
// 2027-03-01 and ends 2028-02-29. On 2027-08-30, 183 days are used and 183 unused:
// 100.01 × 183 ÷ 366 = 50.005 exactly → 50.01. Banker's rounding gives 50.00, and rounding a daily rate first
// (0.27 × 183) gives 49.41.
public sealed class CancelRoundingTests
{
    private static readonly DateOnly Start = new(2027, 3, 1);
    private static readonly DateOnly CancelOn = new(2027, 8, 30);

    [Fact]
    [Trait("Rule", "CANCEL-6")]
    [Trait("Rule", "CANCEL-1c")]
    public void Calculation_of_exactly_half_a_penny_is_rounded_once_away_from_zero()
    {
        var term = new Term(Start, 100.01m, new Payment("PAY-DOM-0042", PaymentMethod.Card, 100.01m));

        var calculation = RefundCalculator.ForTerm(term, CancelOn, hasClaims: false);

        Assert.Equal(new RefundCalculation(50.01m, "CANCEL-1c", 366, 183, 183), calculation);
    }

    // Sold on 2027-02-01. The refund, the term line and the total are the same rounded amount.
    [Fact]
    [Trait("Rule", "CANCEL-6")]
    public void Cancel_with_a_refund_of_exactly_half_a_penny_rounds_away_from_zero()
    {
        var saleDay = new DateOnly(2027, 2, 1);
        var policy = new PolicyBuilder()
            .WithToday(saleDay).WithStartDate(Start).WithAmount(100.01m).WithPaymentMethod(PaymentMethod.Card)
            .Build();

        var result = policy.Cancel(CancelOn, saleDay, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, CancelOn, "CANCEL-1c", 50.01m,
            [new RefundCalculation(50.01m, "CANCEL-1c", 366, 183, 183)],
            [new Refund("REF-DOM-0001", PaymentMethod.Card, 50.01m, "PAY-DOM-0042")]);
    }
}
