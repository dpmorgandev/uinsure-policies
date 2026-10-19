using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2, the E5 ClaimResponse is the claim this request recorded (findings A2/R3 and A2/S1). Whatever happens
// to the policy after this request's save, the 201 answers with its own claim: not the policy's last claim
// when another claim lands straight after it, and not a 500 when the policy can't be read again. The events
// after the save are made by AfterSaveApiFactory at exactly that point.
//   start 2026-10-17 ends 2027-10-16; UK today 2026-11-20.
public sealed class ClaimResponseTests : IClassFixture<AfterSaveApiFactory>
{
    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);
    private static readonly DateTimeOffset ClaimsNow = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly AfterSaveApiFactory _factory;

    public ClaimResponseTests(AfterSaveApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Ours is dated 2026-11-19; the other request's, which commits straight after ours, 2026-11-20. Both are
    // stored, ours first, and the 201 is ours.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_answers_with_its_own_claim_when_another_claim_lands_straight_after_it()
    {
        const string reference = "CLAIM-OWN-RACE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(318.92m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(ClaimsNow);
        _factory.AnotherClaimAfterTheNextSaveOf(reference, new DateOnly(2026, 11, 20), "Fallen tree through the conservatory roof");

        var body = await client.RecordClaim(
            reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 11, 19)).WithDescription("Burst pipe under the kitchen sink").Build());

        var claimReference = ClaimAssert.IsClaim(body, reference, new DateOnly(2026, 11, 19), "Burst pipe under the kitchen sink");
        var policy = await client.ReadBack(reference);
        var otherReference = policy.Member("claims")[1].Member("reference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(otherReference));
        Assert.NotEqual(claimReference, otherReference);
        PolicyStateAssert.ReadsBack(
            policy, sold, reference, TodayStartEnd, "Active", true, null, [],
            [
                new ExpectedClaim(claimReference, new DateOnly(2026, 11, 19), "Burst pipe under the kitchen sink"),
                new ExpectedClaim(otherReference, new DateOnly(2026, 11, 20), "Fallen tree through the conservatory roof"),
            ]);
    }

    // The claim is saved, then the policy can no longer be found in this request: the 201 still answers with
    // the claim recorded, never a 500, and the read-back that follows lists it.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_answers_with_its_own_claim_when_the_policy_cannot_be_read_again_after_the_save()
    {
        const string reference = "CLAIM-OWN-NO-READ";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(447.06m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(ClaimsNow);
        _factory.NothingFoundAfterTheNextSaveOf(reference);

        using var response = await client.PostClaim(
            reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 11, 18)).WithDescription("Kitchen window smashed by a ball").Build());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var claimReference = ClaimAssert.IsClaim(
            await PolicyActions.ClaimedBody(response, reference), reference, new DateOnly(2026, 11, 18), "Kitchen window smashed by a ball");
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active", true, null, [],
            [new ExpectedClaim(claimReference, new DateOnly(2026, 11, 18), "Kitchen window smashed by a ball")]);
    }
}
