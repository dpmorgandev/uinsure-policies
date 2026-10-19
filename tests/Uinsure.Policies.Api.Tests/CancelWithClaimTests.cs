using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2: a policy with a claim is refunded £0 when cancelled, whichever of CANCEL-1a, 1b or 1c would apply.
// The term's ruleApplied is COULD-2 and its refundAmount 0.00, with its day counts as usual, and no Refund
// record is made, so refunds[] is empty in the response and the read-back. hasClaims stays true and the claim
// stays listed. The policy is sold, claimed on (E5) and cancelled (E3), then read back whole:
//   start 2026-10-17 ends 2027-10-16 (365 days).
// No claim can predate the first start (COULD-2b), so the CANCEL-1a case is a policy stored with hasClaims
// true and a start ten days ahead, seeded through the app's IPolicyRepository (PolicyBuilder, PolicySeeder):
//   BuyToLet, auto-renew, Mary Jackson born 1984-04-09, 31 Deansgate, Floor 4, M3 2BA,
//   one term 2026-10-27 to 2027-10-26 for 275.35 paid by Cheque.
public sealed class CancelWithClaimTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public CancelWithClaimTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Claimed and cancelled today, the start date: CANCEL-1b would refund 441.90 in full.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Cancel_in_cooling_off_after_a_claim_refunds_nothing_under_COULD_2()
    {
        const string reference = "COULD2-COOLING-OFF";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(441.90m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        var claimReference = ClaimAssert.IsClaim(
            await client.RecordClaim(reference, new ClaimRequestBuilder().WithDescription("Accidental damage to the television").Build()),
            reference, TestClock.Today, "Accidental damage to the television");

        using var response = await client.PostCancellationWithNoBody(reference);

        _ = CancellationAssert.IsCancellation(await PolicyActions.CancelledBody(response), new ExpectedCancellation(
            reference, TestClock.Today, "COULD-2", "0.00",
            [new ExpectedTerm(1, "COULD-2", 365, 1, 364, "0.00")],
            []));
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Cancelled", true,
            new ExpectedCancellationSummary(TestClock.Today, "COULD-2", "0.00"), [],
            [new ExpectedClaim(claimReference, TestClock.Today, "Accidental damage to the television")]);
    }

    // UK today 2026-12-01 (start + 45), a claim dated 2026-11-20: CANCEL-1c would refund 447.82 (46 days used,
    // 319 unused, of 512.40).
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Cancel_after_cooling_off_after_a_claim_refunds_nothing_under_COULD_2()
    {
        const string reference = "COULD2-AFTER-COOLING";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(512.40m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));
        var claimReference = ClaimAssert.IsClaim(
            await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 11, 20)).WithDescription("Frozen pipe burst in the loft").Build()),
            reference, new DateOnly(2026, 11, 20), "Frozen pipe burst in the loft");

        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 12, 1)).Build());

        _ = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, new DateOnly(2026, 12, 1), "COULD-2", "0.00",
            [new ExpectedTerm(1, "COULD-2", 365, 46, 319, "0.00")],
            []));
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Cancelled", true,
            new ExpectedCancellationSummary(new DateOnly(2026, 12, 1), "COULD-2", "0.00"), [],
            [new ExpectedClaim(claimReference, new DateOnly(2026, 11, 20), "Frozen pipe burst in the loft")]);
    }

    // Seeded with hasClaims true, cancelled today (2026-10-17), before its 2026-10-27 start: CANCEL-1a would
    // refund 275.35 in full.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Cancel_before_the_start_of_a_policy_with_claims_refunds_nothing_under_COULD_2()
    {
        const string reference = "COULD2-BEFORE-START";
        var start = new DateOnly(2026, 10, 27);
        await _factory.Seed(new PolicyBuilder(reference)
            .WithHasClaims(true)
            .WithTerms(new Term(start, 275.35m, new Payment("PAY-COULD2-BEFORE-START-1", PaymentMethod.Cheque, 275.35m)))
            .Build());
        using var client = _factory.CreateClient();

        using var response = await client.PostCancellationWithNoBody(reference);

        _ = CancellationAssert.IsCancellation(await PolicyActions.CancelledBody(response), new ExpectedCancellation(
            reference, TestClock.Today, "COULD-2", "0.00",
            [new ExpectedTerm(1, "COULD-2", 365, 0, 365, "0.00")],
            []));
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), SeededSale.For(reference, start, 275.35m, "Cheque"), reference, new DateOnly(2027, 10, 26), "Cancelled", true,
            new ExpectedCancellationSummary(TestClock.Today, "COULD-2", "0.00"), [], []);
    }
}
