using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-3: an auto-renewing policy's renewal cannot be paid by Cheque, whether the Cheque is sent or defaulted from
// the current term's payment (RENEW-3a): a 422 COULD-3 violation with the extension paymentMethod Cheque, and the
// policy keeps its one term. For a defaulted Cheque the message asks for another method, naming Card and
// DirectDebit. Sending another method renews a Cheque policy. (Without auto-renew no payment is taken, so COULD-3
// does not apply: RenewUnpaidTests.) Renewed on 2027-10-20, inside the window of the builder's default sale:
//   start 2026-11-08 ends 2027-11-07; term 2 2027-11-08 to 2028-11-07.
public sealed class RenewChequeTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);

    [Fact]
    [Trait("Rule", "COULD-3")]
    public void Auto_renewal_with_an_explicit_Cheque_is_rejected()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.DirectDebit).Build();

        var result = policy.Renew(569.73m, PaymentMethod.Cheque, RenewalDay, new PaymentReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-3");
        Assert.Equal(PaymentMethod.Cheque, ViolationAssert.Extension(violation, "paymentMethod"));
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_defaulting_to_the_current_terms_Cheque_is_rejected_asking_for_another_method()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Cheque).Build();

        var result = policy.Renew(569.73m, null, RenewalDay, new PaymentReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-3");
        Assert.Equal(PaymentMethod.Cheque, ViolationAssert.Extension(violation, "paymentMethod"));
        Assert.Contains("Card", violation.Message, StringComparison.Ordinal);
        Assert.Contains("DirectDebit", violation.Message, StringComparison.Ordinal);
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_of_a_Cheque_policy_by_Card_is_accepted()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Cheque).Build();

        var result = policy.Renew(569.73m, PaymentMethod.Card, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 569.73m,
            new Payment("PAY-DOM-R0001", PaymentMethod.Card, 569.73m));
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    public void Auto_renewal_of_a_Cheque_policy_by_DirectDebit_is_accepted()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Cheque).Build();

        var result = policy.Renew(569.73m, PaymentMethod.DirectDebit, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 569.73m,
            new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 569.73m));
    }

    // Term 1 (2026-03-01 to 2027-02-28) was paid by Cheque and term 2 (2027-03-01 to 2028-02-29) by Card. Today,
    // 2026-10-17, is in term 1, so the defaulted method is its Cheque, not the latest term's Card: COULD-3 is
    // listed beside RENEW-1/2 (window 2028-01-30 to 2028-02-29).
    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    [Trait("Rule", "RENEW-1")]
    public void Auto_renewal_defaulting_from_the_term_covering_today_reports_its_Cheque_beside_the_window()
    {
        var policy = new PolicyBuilder().RestoreWithTerms(
            new Term(new DateOnly(2026, 3, 1), 365.00m, new Payment("PAY-DOM-0042", PaymentMethod.Cheque, 365.00m)),
            new Term(new DateOnly(2027, 3, 1), 512.40m, new Payment("PAY-DOM-0042-R1", PaymentMethod.Card, 512.40m)));

        var result = policy.Renew(569.73m, null, TestClock.Today, new PaymentReferences().Next);

        ViolationAssert.BusinessRules(result, "RENEW-1/2", "COULD-3");
        Assert.Equal(PaymentMethod.Cheque, ViolationAssert.Extension(result.Violations[1], "paymentMethod"));
        Assert.Equal(2, policy.Terms.Count);
    }
}
