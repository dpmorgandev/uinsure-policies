using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-3b: a policy that does not auto-renew is renewed unpaid: the new term has no payment, and collecting it is
// out of scope. Sending a method anyway is a 422 RENEW-3b violation (no extension), and the policy keeps its one
// term. As no payment is taken, nothing defaults from the current term and COULD-3 does not apply: a Cheque sent
// is RENEW-3b alone, and a Cheque policy renews unpaid. Renewed on 2027-10-20, inside the window of the
// builder's default sale with auto-renew off:
//   start 2026-11-08 ends 2027-11-07; term 2 2027-11-08 to 2028-11-07.
public sealed class RenewUnpaidTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public void Renewal_without_auto_renew_and_without_a_method_creates_an_unpaid_term()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(581.37m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 581.37m, null);
        Assert.Equal(2, policy.Terms.Count);
        Assert.Same(policy.LatestTerm, result.Value);
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public void Renewal_without_auto_renew_with_a_Card_method_is_rejected()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(581.37m, PaymentMethod.Card, RenewalDay, new PaymentReferences().Next);

        ViolationAssert.SingleBusinessRule(result, "RENEW-3b");
        Assert.Single(policy.Terms);
    }

    // The method the policy was sold with is still a method sent.
    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public void Renewal_without_auto_renew_with_the_sold_method_is_rejected()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).WithPaymentMethod(PaymentMethod.DirectDebit).Build();

        var result = policy.Renew(581.37m, PaymentMethod.DirectDebit, RenewalDay, new PaymentReferences().Next);

        ViolationAssert.SingleBusinessRule(result, "RENEW-3b");
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "COULD-3")]
    public void Renewal_without_auto_renew_with_a_Cheque_is_RENEW_3b_alone_not_COULD_3()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(581.37m, PaymentMethod.Cheque, RenewalDay, new PaymentReferences().Next);

        ViolationAssert.SingleBusinessRule(result, "RENEW-3b");
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "COULD-3")]
    public void Renewal_without_auto_renew_of_a_Cheque_policy_is_unpaid_and_not_COULD_3()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).WithPaymentMethod(PaymentMethod.Cheque).Build();

        var result = policy.Renew(581.37m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 581.37m, null);
    }
}
