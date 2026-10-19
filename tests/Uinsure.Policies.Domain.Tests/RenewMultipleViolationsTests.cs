using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// Renew's 422 rules are all evaluated and none returns early: every one broken is listed, in rule-ID order
// (RENEW-1/2, RENEW-3b, RENEW-5, COULD-3), and the policy keeps its one term. The builder's default sale:
//   start 2026-11-08 ends 2027-11-07; window 2027-10-08 to 2027-11-07.
public sealed class RenewMultipleViolationsTests
{
    // Today (2026-10-17) is before the window; no auto-renew, a method sent, amount 0.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "RENEW-5")]
    public void Renewal_before_the_window_without_auto_renew_with_a_method_and_amount_0_reports_all_three()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(0.00m, PaymentMethod.Card, TestClock.Today, new PaymentReferences().Next);

        ViolationAssert.BusinessRules(result, "RENEW-1/2", "RENEW-3b", "RENEW-5");
        Assert.Single(policy.Terms);
    }

    // Today (2026-10-17) is before the window; auto-renew, an explicit Cheque, a negative amount.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    [Trait("Rule", "RENEW-5")]
    [Trait("Rule", "COULD-3")]
    public void Auto_renewal_before_the_window_by_Cheque_for_a_negative_amount_reports_all_three()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(-1.00m, PaymentMethod.Cheque, TestClock.Today, new PaymentReferences().Next);

        ViolationAssert.BusinessRules(result, "RENEW-1/2", "RENEW-5", "COULD-3");
        Assert.Single(policy.Terms);
    }

    // In the window (2027-10-20), amount 0 and a Cheque defaulted from the sold term.
    [Fact]
    [Trait("Rule", "RENEW-5")]
    [Trait("Rule", "COULD-3")]
    public void Auto_renewal_in_the_window_for_0_with_a_defaulted_Cheque_reports_RENEW_5_and_COULD_3()
    {
        var policy = new PolicyBuilder().WithPaymentMethod(PaymentMethod.Cheque).Build();

        var result = policy.Renew(0.00m, null, new DateOnly(2027, 10, 20), new PaymentReferences().Next);

        ViolationAssert.BusinessRules(result, "RENEW-5", "COULD-3");
        Assert.Single(policy.Terms);
    }

    // The day after the end (2027-11-08); no auto-renew, a method sent.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    [Trait("Rule", "RENEW-3b")]
    public void Renewal_after_the_end_without_auto_renew_with_a_method_reports_RENEW_1_2_and_RENEW_3b()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(574.12m, PaymentMethod.DirectDebit, new DateOnly(2027, 11, 8), new PaymentReferences().Next);

        ViolationAssert.BusinessRules(result, "RENEW-1/2", "RENEW-3b");
        Assert.Single(policy.Terms);
    }
}
