using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2a: a claim date may not be after UK today, else 422 with today as an extension. Today itself is
// accepted (ClaimRecordTests); tomorrow is not. At 23:30 UTC in BST the UK date is the next UTC date, and that
// date is today. A rejected claim leaves the policy as sold (hasClaims false, no claims).
//   start 2026-10-17 ends 2027-10-16; start 2027-06-01 ends 2028-05-31 (sold with the clock at 2027-06-01).
public sealed class ClaimDateTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Claim date in the future";

    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public ClaimDateTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_tomorrow_is_rejected_as_in_the_future()
    {
        const string reference = "CLAIM-TOMORROW";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(367.21m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().WithClaimDate(TestClock.Today.AddDays(1)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-2a", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("today"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }

    // 2027-06-30T23:30Z is 00:30 BST on 2027-07-01: a claim dated the UK date is today, not tomorrow.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_the_UK_date_at_23_30_UTC_in_BST_is_accepted_as_today()
    {
        const string reference = "CLAIM-BST-UK";
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2027, 6, 1)).WithAmount(318.07m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 30, 23, 30, 0, TimeSpan.Zero));

        var claim = await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2027, 7, 1)).WithDescription("Fire in the kitchen").Build());

        var claimReference = ClaimAssert.IsClaim(claim, reference, new DateOnly(2027, 7, 1), "Fire in the kitchen");
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2028, 5, 31), "Active", true, null, [],
            [new ExpectedClaim(claimReference, new DateOnly(2027, 7, 1), "Fire in the kitchen")]);
    }

    // 2027-11-01 is after today and after the 2027-10-16 end: both rules are reported, COULD-2a first.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_dated_after_today_and_after_the_end_of_cover_reports_COULD_2a_and_COULD_2b()
    {
        const string reference = "CLAIM-BOTH";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(394.72m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2027, 11, 1)).Build());

        await ProblemAssert.MultipleRuleViolations(response, Title, PolicyActions.ClaimsPath(reference), "COULD-2a", "COULD-2b");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }
}
