using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-6: payment at sale. The sale's term carries a Payment for the full policy amount, by the method
// sold with, under the generated reference the service passes in.
public sealed class SellPaymentTests
{
    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Sale_creates_a_payment_for_the_full_amount_on_its_term()
    {
        var policy = new PolicyBuilder().WithAmount(389.50m).Build();

        var term = Assert.Single(policy.Terms);
        Assert.NotNull(term.Payment);
        Assert.Equal(389.50m, term.Payment.Amount);
    }

    [Theory]
    [Trait("Rule", "SELL-6")]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.DirectDebit)]
    [InlineData(PaymentMethod.Cheque)]
    public void Sale_payment_is_by_the_method_sold_with(PaymentMethod method)
    {
        var policy = new PolicyBuilder().WithPaymentMethod(method).Build();

        var payment = Assert.Single(policy.Terms).Payment;
        Assert.NotNull(payment);
        Assert.Equal(method, payment.Method);
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Sale_payment_carries_the_generated_reference_passed_in()
    {
        var policy = new PolicyBuilder().WithPaymentReference("PAY-7Q2M9X4K").Build();

        var payment = Assert.Single(policy.Terms).Payment;
        Assert.NotNull(payment);
        Assert.Equal("PAY-7Q2M9X4K", payment.Reference);
    }
}
