using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-2, recording a claim: policy.RecordClaim(claimDate, description, today) records it (the description is
// optional) and sets HasClaims, which is false at sale. The £0-refund half of COULD-2 is CancelWithClaimTests;
// the date rules are ClaimDateTests (COULD-2a), ClaimCoverTests (COULD-2b) and ClaimOnCancelledTests (COULD-2c).
//   start 2026-10-17 (today) ends 2027-10-16.
public sealed class ClaimRecordTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // Dated today, the start date: neither after today (COULD-2a) nor before cover (COULD-2b).
    [Fact]
    [Trait("Rule", "COULD-2")]
    [Trait("Rule", "COULD-2a")]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_today_on_the_start_date_is_recorded_and_sets_HasClaims()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();

        var result = policy.RecordClaim(Today, "Storm damage to the garage roof", Today);

        ClaimAssert.IsClaim(result, Today, "Storm damage to the garage roof");
        Assert.True(policy.HasClaims);
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Claim_without_a_description_is_recorded_with_none()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();

        var result = policy.RecordClaim(Today, null, Today);

        ClaimAssert.IsClaim(result, Today, null);
        Assert.True(policy.HasClaims);
    }
}
