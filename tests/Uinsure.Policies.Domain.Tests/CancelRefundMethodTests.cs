using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-2: each refund goes to the method of the payment it reverses, and names that payment's reference. The
// refund's own reference comes from the source the caller hands to Cancel, in order.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, 527.18; cancelled today, before the start,
//   it refunds in full (CANCEL-1a).
public sealed class CancelRefundMethodTests
{
    private static readonly DateOnly Today = TestClock.Today;

    [Theory]
    [Trait("Rule", "CANCEL-2")]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.DirectDebit)]
    [InlineData(PaymentMethod.Cheque)]
    public void Refund_goes_to_the_method_of_the_payment_it_reverses(PaymentMethod method)
    {
        var policy = new PolicyBuilder().WithPaymentMethod(method).WithPaymentReference("PAY-DOM-METHOD").Build();

        var result = policy.Cancel(Today, Today, new RefundReferences().Next);

        var refund = Assert.Single(result.Value.Refunds);
        Assert.Equal(new Refund("REF-DOM-0001", method, 527.18m, "PAY-DOM-METHOD"), refund);
    }

    // Two terms (RestoredPolicy.CardThenDirectDebit), cancelled today: term 1 is pro-rata (231 days used,
    // 365.00 × 134 ÷ 365 = 134.00) back to its Card; term 2 hasn't started (512.40 in full) back to its DirectDebit.
    [Fact]
    [Trait("Rule", "CANCEL-2")]
    public void Each_refund_goes_to_the_method_of_its_own_terms_payment()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();

        var result = policy.Cancel(Today, Today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new RefundCalculation(134.00m, "CANCEL-1c", 365, 231, 134), new RefundCalculation(512.40m, "CANCEL-1a", 366, 0, 366)],
            result.Value.Terms);
        Assert.Equal(
            [
                new Refund("REF-DOM-0001", PaymentMethod.Card, 134.00m, "PAY-DOM-0042"),
                new Refund("REF-DOM-0002", PaymentMethod.DirectDebit, 512.40m, "PAY-DOM-0042-R1"),
            ],
            result.Value.Refunds);
    }
}
