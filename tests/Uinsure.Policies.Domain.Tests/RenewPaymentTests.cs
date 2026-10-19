using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-3a: an auto-renewing policy's renewal creates a payment, by the method sent, else by the current term's
// payment method. The payment's reference comes from the source handed to Renew, and its amount is the new
// term's amount: so a later refund of that term, which is at most the term's amount, is at most what was paid.
// Renewed on 2027-10-20, inside the window of the builder's default sale:
//   start 2026-11-08 ends 2027-11-07 (DirectDebit unless overridden); term 2 2027-11-08 to 2028-11-07.
// RestoredPolicy.CardThenDirectDebit: 2026-03-01 to 2027-02-28 by Card, then 2027-03-01 to 2028-02-29 by
// DirectDebit; renewed on 2028-02-01 (window 2028-01-30 to 2028-02-29), term 3 runs 2028-03-01 to 2029-02-28.
public sealed class RenewPaymentTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);
    private static readonly DateOnly RenewedStart = new(2027, 11, 8);
    private static readonly DateOnly RenewedEnd = new(2028, 11, 7);

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_without_a_method_is_paid_by_the_current_terms_method()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Card).Build();

        var result = policy.Renew(573.46m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, RenewedStart, RenewedEnd, 573.46m, new Payment("PAY-DOM-R0001", PaymentMethod.Card, 573.46m));
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_with_a_method_is_paid_by_that_method()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.DirectDebit).Build();

        var result = policy.Renew(573.46m, PaymentMethod.Card, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, RenewedStart, RenewedEnd, 573.46m, new Payment("PAY-DOM-R0001", PaymentMethod.Card, 573.46m));
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_of_a_Card_policy_with_DirectDebit_is_paid_by_DirectDebit()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Card).Build();

        var result = policy.Renew(573.46m, PaymentMethod.DirectDebit, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, RenewedStart, RenewedEnd, 573.46m, new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 573.46m));
    }

    // The payment is for the new term's 612.47, not the sold term's 527.18.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Renewal_payment_is_for_the_new_terms_amount()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(612.47m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(612.47m, result.Value.Amount);
        Assert.NotNull(result.Value.Payment);
        Assert.Equal(612.47m, result.Value.Payment.Amount);
    }

    // The sold term keeps its own payment.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Renewal_payment_leaves_the_sold_terms_payment_as_it_was()
    {
        var policy = new PolicyBuilder().WithPaymentReference("PAY-DOM-SOLD").Build();

        Assert.True(policy.Renew(612.47m, PaymentMethod.Card, RenewalDay, new PaymentReferences().Next).IsSuccess);

        TermAssert.Is(policy.Terms[0], new DateOnly(2026, 11, 8), new DateOnly(2027, 11, 7), 527.18m,
            new Payment("PAY-DOM-SOLD", PaymentMethod.DirectDebit, 527.18m));
    }

    // Today (2028-02-01) is in term 2, paid by DirectDebit: the default is its method, not term 1's Card.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_without_a_method_is_paid_by_the_method_of_the_term_covering_today_not_the_first()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();

        var result = policy.Renew(538.90m, null, new DateOnly(2028, 2, 1), new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2028, 3, 1), new DateOnly(2029, 2, 28), 538.90m,
            new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 538.90m));
    }

    // Renewed by Card on 2027-10-20, then again without a method on 2028-10-20, in term 2: paid by Card.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public void Second_auto_renewal_without_a_method_is_paid_by_the_renewed_terms_method()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.DirectDebit).Build();
        var references = new PaymentReferences();
        Assert.True(policy.Renew(573.46m, PaymentMethod.Card, RenewalDay, references.Next).IsSuccess);

        var result = policy.Renew(589.12m, null, new DateOnly(2028, 10, 20), references.Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2028, 11, 8), new DateOnly(2029, 11, 7), 589.12m,
            new Payment("PAY-DOM-R0002", PaymentMethod.Card, 589.12m));
    }
}
