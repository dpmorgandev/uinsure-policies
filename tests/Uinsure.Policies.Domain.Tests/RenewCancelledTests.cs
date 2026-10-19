using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-6: a cancelled policy cannot be renewed: a 409 conflict with the extension cancellationDate. A conflict
// short-circuits, so it is returned alone, before any 422 renewal rule, and the policy keeps its one term. The
// builder's default sale (start 2026-11-08, ends 2027-11-07, window 2027-10-08 to 2027-11-07) is cancelled on
// TestClock.Today (2026-10-17) with effect from 2027-10-25, a date inside the renewal window.
public sealed class RenewCancelledTests
{
    private static readonly DateOnly CancelledFrom = new(2027, 10, 25);

    private static Policy CancelledPolicy(PolicyBuilder builder)
    {
        var policy = builder.Build();
        Assert.True(policy.Cancel(CancelledFrom, TestClock.Today, new RefundReferences().Next).IsSuccess);
        return policy;
    }

    // In the window, a valid amount, the sold method: every 422 rule passes, so only the conflict is left.
    [Fact]
    [Trait("Rule", "RENEW-6")]
    public void Renewal_of_a_cancelled_policy_is_a_conflict_giving_the_cancellation_date()
    {
        var policy = CancelledPolicy(new PolicyBuilder());

        var result = policy.Renew(557.64m, null, new DateOnly(2027, 10, 20), new PaymentReferences().Next);

        var violation = ViolationAssert.SingleConflict(result, "RENEW-6");
        Assert.Equal(CancelledFrom, ViolationAssert.Extension(violation, "cancellationDate"));
        Assert.Single(policy.Terms);
    }

    // Out of the window, amount 0 and an explicit Cheque would be RENEW-1/2, RENEW-5 and COULD-3.
    [Fact]
    [Trait("Rule", "RENEW-6")]
    public void Renewal_of_a_cancelled_policy_breaking_422_rules_is_only_the_conflict()
    {
        var policy = CancelledPolicy(new PolicyBuilder());

        var result = policy.Renew(0.00m, PaymentMethod.Cheque, TestClock.Today, new PaymentReferences().Next);

        var violation = ViolationAssert.SingleConflict(result, "RENEW-6");
        Assert.Equal(CancelledFrom, ViolationAssert.Extension(violation, "cancellationDate"));
        Assert.Single(policy.Terms);
    }

    // Without auto-renew, a method sent would be RENEW-3b.
    [Fact]
    [Trait("Rule", "RENEW-6")]
    public void Renewal_of_a_cancelled_policy_without_auto_renew_sending_a_method_is_only_the_conflict()
    {
        var policy = CancelledPolicy(new PolicyBuilder().WithAutoRenew(false));

        var result = policy.Renew(557.64m, PaymentMethod.Card, new DateOnly(2027, 10, 20), new PaymentReferences().Next);

        ViolationAssert.SingleConflict(result, "RENEW-6");
        Assert.Single(policy.Terms);
    }
}
