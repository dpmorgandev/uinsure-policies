using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-2a: a claim date may not be after today, else 422 with the extension today. Today itself is accepted
// (ClaimRecordTests). COULD-2a and COULD-2b are both collected; neither returns early. A rejected claim leaves
// HasClaims false.
public sealed class ClaimDateTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // In cover (2026-03-01 to 2027-02-28), but tomorrow: COULD-2a alone.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    public void Claim_dated_tomorrow_is_rejected_as_future()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2026, 3, 1));

        var result = policy.RecordClaim(Today.AddDays(1), "Storm damage to the garage roof", Today);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-2a");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "today"));
        Assert.False(policy.HasClaims);
    }

    // The builder's default starts 2026-11-08 and ends 2027-11-07: tomorrow is after today and before cover. Both
    // are reported, COULD-2a first.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_after_today_and_before_cover_reports_COULD_2a_and_COULD_2b()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.RecordClaim(Today.AddDays(1), "Storm damage to the garage roof", Today);

        ViolationAssert.BusinessRules(result, "COULD-2a", "COULD-2b");
        Assert.Equal(Today, ViolationAssert.Extension(result.Violations[0], "today"));
        Assert.Equal(new DateOnly(2026, 11, 8), ViolationAssert.Extension(result.Violations[1], "coverStart"));
        Assert.Equal(new DateOnly(2027, 11, 7), ViolationAssert.Extension(result.Violations[1], "coverEnd"));
        Assert.False(policy.HasClaims);
    }
}
