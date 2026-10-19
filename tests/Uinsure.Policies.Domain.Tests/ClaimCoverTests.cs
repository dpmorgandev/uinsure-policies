using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-2b: the claim date must be within some term's cover (start to end inclusive), else 422 with coverStart
// (the first term's start) and coverEnd (the latest term's end). The start date itself is accepted
// (ClaimRecordTests); each other side of each edge is here. A rejected claim leaves HasClaims false.
//   start 2026-10-17 (today) ends 2027-10-16; start 2025-10-17 ends 2026-10-16 (yesterday).
public sealed class ClaimCoverTests
{
    private static readonly DateOnly Today = TestClock.Today;

    [Fact]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_the_day_before_the_start_is_rejected_as_outside_cover()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).Build();

        var result = policy.RecordClaim(Today.AddDays(-1), "Storm damage to the garage roof", Today);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-2b");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "coverStart"));
        Assert.Equal(new DateOnly(2027, 10, 16), ViolationAssert.Extension(violation, "coverEnd"));
        Assert.False(policy.HasClaims);
    }

    // The term ended yesterday; its last day is still cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_the_last_day_of_cover_is_recorded()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 17));

        var result = policy.RecordClaim(new DateOnly(2026, 10, 16), "Bicycle stolen from the shed", Today);

        ClaimAssert.IsClaim(result, new DateOnly(2026, 10, 16), "Bicycle stolen from the shed");
        Assert.True(policy.HasClaims);
    }

    // As above; today is the day after the end, so not in the future but outside cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_the_day_after_the_end_is_rejected_as_outside_cover()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 17));

        var result = policy.RecordClaim(Today, "Bicycle stolen from the shed", Today);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-2b");
        Assert.Equal(new DateOnly(2025, 10, 17), ViolationAssert.Extension(violation, "coverStart"));
        Assert.Equal(new DateOnly(2026, 10, 16), ViolationAssert.Extension(violation, "coverEnd"));
        Assert.False(policy.HasClaims);
    }

    // Two terms, 2025-10-17 to 2026-10-16 and 2026-10-17 to 2027-10-16: today is in the second, and a claim dated
    // in the first is within a term's cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_in_an_earlier_term_is_recorded()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 17), new DateOnly(2026, 10, 17));

        var result = policy.RecordClaim(new DateOnly(2026, 1, 15), "Bicycle stolen from the shed", Today);

        ClaimAssert.IsClaim(result, new DateOnly(2026, 1, 15), "Bicycle stolen from the shed");
    }

    [Fact]
    [Trait("Rule", "COULD-2b")]
    public void Claim_dated_before_the_first_of_two_terms_gives_the_whole_cover()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 17), new DateOnly(2026, 10, 17));

        var result = policy.RecordClaim(new DateOnly(2025, 10, 16), "Bicycle stolen from the shed", Today);

        var violation = ViolationAssert.SingleBusinessRule(result, "COULD-2b");
        Assert.Equal(new DateOnly(2025, 10, 17), ViolationAssert.Extension(violation, "coverStart"));
        Assert.Equal(new DateOnly(2027, 10, 16), ViolationAssert.Extension(violation, "coverEnd"));
    }
}
