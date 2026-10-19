using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// Status (requirements, the row after GET-1): Cancelled overrides the derived status as soon as the cancellation
// exists, whatever its date. Traited GET-1, like PolicyStatusTests, which hold the other statuses.
public sealed class CancelledStatusTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // Cancelled today, before the 2026-11-08 start: Cancelled, not Pending.
    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_of_a_policy_cancelled_before_its_start_is_Cancelled()
    {
        var policy = new PolicyBuilder().Build();

        Assert.True(policy.Cancel(null, Today, new RefundReferences().Next).IsSuccess);

        Assert.Equal(PolicyStatus.Cancelled, policy.StatusOn(Today));
    }

    // Started today and cancelled with effect from 2026-12-15: on 2026-11-01, in cover, Cancelled, not Active.
    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_of_a_policy_cancelled_from_a_future_date_is_Cancelled_while_in_cover()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();

        Assert.True(policy.Cancel(new DateOnly(2026, 12, 15), Today, new RefundReferences().Next).IsSuccess);

        Assert.Equal(PolicyStatus.Cancelled, policy.StatusOn(new DateOnly(2026, 11, 1)));
    }
}
