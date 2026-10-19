using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-2c: no claim on a cancelled policy: a 409 conflict with the extension cancellationDate. A conflict
// short-circuits, so it is returned before the 422 date rules, and HasClaims stays false.
//   start 2026-10-17 (today) ends 2027-10-16.
public sealed class ClaimOnCancelledTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // Cancelled with effect from 2026-12-15, then a claim dated today, inside cover.
    [Fact]
    [Trait("Rule", "COULD-2c")]
    public void Claim_on_a_cancelled_policy_is_a_conflict_giving_the_cancellation_date()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();
        Assert.True(policy.Cancel(new DateOnly(2026, 12, 15), Today, new RefundReferences().Next).IsSuccess);

        var result = policy.RecordClaim(Today, "Storm damage to the garage roof", Today);

        var violation = ViolationAssert.SingleConflict(result, "COULD-2c");
        Assert.Equal(new DateOnly(2026, 12, 15), ViolationAssert.Extension(violation, "cancellationDate"));
        Assert.False(policy.HasClaims);
    }

    // Cancelled today; tomorrow would be COULD-2a, but the conflict is returned alone.
    [Fact]
    [Trait("Rule", "COULD-2c")]
    public void Claim_dated_tomorrow_on_a_cancelled_policy_is_only_the_conflict()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();
        Assert.True(policy.Cancel(Today, Today, new RefundReferences().Next).IsSuccess);

        var result = policy.RecordClaim(Today.AddDays(1), "Storm damage to the garage roof", Today);

        var violation = ViolationAssert.SingleConflict(result, "COULD-2c");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "cancellationDate"));
    }
}
