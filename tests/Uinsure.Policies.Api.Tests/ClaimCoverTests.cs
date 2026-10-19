using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2b: the claim date must be inside a term's cover (start to end inclusive), else 422 with coverStart and
// coverEnd. The start date itself is accepted (ClaimRecordTests); each other side of each edge is here. A
// rejected claim leaves the policy as sold (hasClaims false, no claims).
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days).
public sealed class ClaimCoverTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Claim date outside cover";

    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);
    private static readonly DateOnly FutureStart = new(2026, 11, 8);
    private static readonly DateOnly FutureStartEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public ClaimCoverTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // The policy started today; yesterday is before its cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_dated_the_day_before_the_start_is_rejected_as_outside_cover()
    {
        const string reference = "CLAIM-BEFORE-START";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(425.30m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().WithClaimDate(TestClock.Today.AddDays(-1)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-2b", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("coverStart"));
        Assert.Equal(TodayStartEnd, problem.GetDate("coverEnd"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }

    // The policy starts 2026-11-08; a claim dated today (2026-10-17) is before any cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_on_a_policy_not_yet_started_is_rejected_as_outside_cover()
    {
        const string reference = "CLAIM-NOT-STARTED";
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(FutureStart).WithAmount(261.94m).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-2b", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(FutureStart, problem.GetDate("coverStart"));
        Assert.Equal(FutureStartEnd, problem.GetDate("coverEnd"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, FutureStartEnd, "Pending");
    }

    // UK today 2027-11-20, after the 2027-11-07 end: a claim dated the last day of cover is inside it.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_dated_the_last_day_of_cover_is_accepted_after_the_policy_expired()
    {
        const string reference = "CLAIM-LAST-DAY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(349.56m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 11, 20, 12, 0, 0, TimeSpan.Zero));

        var claim = await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(FutureStartEnd).WithDescription("Subsidence crack in the wall").Build());

        var claimReference = ClaimAssert.IsClaim(claim, reference, FutureStartEnd, "Subsidence crack in the wall");
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, FutureStartEnd, "Expired", true, null, [],
            [new ExpectedClaim(claimReference, FutureStartEnd, "Subsidence crack in the wall")]);
    }

    // UK today 2027-11-20: 2027-11-08 is the day after the end, in the past but outside cover.
    [Fact]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_dated_the_day_after_the_end_of_cover_is_rejected_as_outside_cover()
    {
        const string reference = "CLAIM-END-PLUS-1";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(287.13m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 11, 20, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2027, 11, 8)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-2b", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(FutureStart, problem.GetDate("coverStart"));
        Assert.Equal(FutureStartEnd, problem.GetDate("coverEnd"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, FutureStartEnd, "Expired");
    }
}
