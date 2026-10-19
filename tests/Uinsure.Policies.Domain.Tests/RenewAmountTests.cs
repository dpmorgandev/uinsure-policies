using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-5: the client supplies the new term's amount, which must be greater than 0, else a 422 RENEW-5 violation
// with the extension amount, and the policy keeps its one term. 0.00 and 0.01 are the two sides of the boundary.
// Renewed on 2027-10-20, inside the window of the builder's default sale:
//   start 2026-11-08 ends 2027-11-07; term 2 2027-11-08 to 2028-11-07.
public sealed class RenewAmountTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public void Renewal_amount_0_is_rejected()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(0.00m, null, RenewalDay, new PaymentReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "RENEW-5");
        Assert.Equal(0.00m, ViolationAssert.Extension(violation, "amount"));
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public void Renewal_amount_0_01_is_accepted()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(0.01m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 0.01m,
            new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 0.01m));
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public void Renewal_amount_below_0_is_rejected()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(-12.50m, null, RenewalDay, new PaymentReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "RENEW-5");
        Assert.Equal(-12.50m, ViolationAssert.Extension(violation, "amount"));
        Assert.Single(policy.Terms);
    }

    // RENEW-5 is checked on an unpaid renewal too.
    [Fact]
    [Trait("Rule", "RENEW-5")]
    public void Renewal_amount_0_without_auto_renew_is_rejected()
    {
        var policy = new PolicyBuilder().WithAutoRenew(false).Build();

        var result = policy.Renew(0.00m, null, RenewalDay, new PaymentReferences().Next);

        ViolationAssert.SingleBusinessRule(result, "RENEW-5");
        Assert.Single(policy.Terms);
    }
}
