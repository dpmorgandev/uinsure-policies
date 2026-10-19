using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2, recording a claim: E5 POST /policies/{reference}/claims → 201, Location /policies/{reference}, and the
// ClaimResponse; then the E2 read-back. claimDate is required and description optional (at most 500 characters);
// any claim sets hasClaims, which is false at sale. A body that breaks the shape is a 400 with no ruleId, and
// nothing is recorded. An unknown reference is 404 GET-1. The £0-refund half of COULD-2 is CancelWithClaimTests.
//   start 2026-10-17 ends 2027-10-16 (365 days).
public sealed class ClaimRecordTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public ClaimRecordTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Claimed today, on the start date: neither in the future (COULD-2a) nor before cover (COULD-2b).
    [Fact]
    [Trait("Rule", "COULD-2")]
    [Trait("Rule", "COULD-2a")]
    [Trait("Rule", "COULD-2b")]
    public async Task Claim_dated_today_on_the_start_date_is_recorded_and_sets_hasClaims()
    {
        const string reference = "CLAIM-TODAY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(389.17m).WithPaymentMethod("Card").Build();

        await AssertClaimed(
            reference, sold,
            new ClaimRequestBuilder().WithClaimDate(TestClock.Today).WithDescription("Escape of water from the boiler").Build(),
            TestClock.Today, "Escape of water from the boiler");
    }

    // UK today 2026-11-03; the claim is dated 2026-11-01 and has no description.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_without_a_description_is_recorded_with_a_null_description()
    {
        const string reference = "CLAIM-NO-DESC";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(402.05m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

        var claim = await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 11, 1)).WithoutDescription().Build());

        var claimReference = ClaimAssert.IsClaim(claim, reference, new DateOnly(2026, 11, 1), null);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active", true, null, [],
            [new ExpectedClaim(claimReference, new DateOnly(2026, 11, 1), null)]);
    }

    // Two claims, on two UK todays: both are stored, in the order recorded, each with its own reference.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Second_claim_is_listed_after_the_first()
    {
        const string reference = "CLAIM-TWO";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(538.64m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var first = ClaimAssert.IsClaim(
            await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 11, 14)).WithDescription("Storm damage to the roof tiles").Build()),
            reference, new DateOnly(2026, 11, 14), "Storm damage to the roof tiles");
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var second = ClaimAssert.IsClaim(
            await client.RecordClaim(reference, new ClaimRequestBuilder().WithClaimDate(new DateOnly(2026, 12, 9)).WithDescription("Bicycle stolen from the shed").Build()),
            reference, new DateOnly(2026, 12, 9), "Bicycle stolen from the shed");

        Assert.NotEqual(first, second);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active", true, null, [],
            [
                new ExpectedClaim(first, new DateOnly(2026, 11, 14), "Storm damage to the roof tiles"),
                new ExpectedClaim(second, new DateOnly(2026, 12, 9), "Bicycle stolen from the shed"),
            ]);
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_description_of_500_characters_is_accepted()
    {
        const string reference = "CLAIM-DESC-500";
        var description = Description(500);
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(276.48m).WithPaymentMethod("DirectDebit").Build();

        await AssertClaimed(reference, sold, new ClaimRequestBuilder().WithDescription(description).Build(), TestClock.Today, description);
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_description_of_501_characters_is_rejected_with_400()
    {
        await AssertClaimShape("CLAIM-DESC-501", new ClaimRequestBuilder().WithDescription(Description(501)).Build().ToJsonString(), "description");
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_without_a_claimDate_is_rejected_with_400()
    {
        var body = new ClaimRequestBuilder().Build();
        body.Remove("claimDate");

        await AssertClaimShape("CLAIM-NO-DATE", body.ToJsonString(), "claimDate");
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_with_a_claimDate_not_in_yyyy_MM_dd_is_rejected_with_400()
    {
        var body = new ClaimRequestBuilder().Build();
        body["claimDate"] = "17/10/2026";

        await AssertClaimShape("CLAIM-BAD-DATE", body.ToJsonString(), "$.claimDate");
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_with_an_unknown_field_is_rejected_with_400()
    {
        var body = new ClaimRequestBuilder().Build();
        body["amount"] = 1200.00m;

        await AssertClaimShape("CLAIM-UNKNOWN", body.ToJsonString(), "$.amount");
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public async Task Claim_with_malformed_JSON_is_rejected_with_400()
    {
        await AssertClaimShape("CLAIM-MALFORMED", "{\"claimDate\": \"2026-10-17\"", errorKey: null);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Claim_on_an_unknown_reference_answers_404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostClaim("CLAIM-NO-SUCH", new ClaimRequestBuilder().Build());

        await ProblemAssert.NotFound(response, PolicyActions.ClaimsPath("CLAIM-NO-SUCH"), "CLAIM-NO-SUCH");
    }

    // A fixed, readable description of exactly the given length (no random data).
    private static string Description(int length) =>
        string.Concat(Enumerable.Repeat("Water came through the ceiling. ", 20))[..length];

    private async Task AssertClaimed(string reference, JsonObject sold, JsonObject claim, DateOnly expectedDate, string expectedDescription)
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        var body = await client.RecordClaim(reference, claim);

        var claimReference = ClaimAssert.IsClaim(body, reference, expectedDate, expectedDescription);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active", true, null, [],
            [new ExpectedClaim(claimReference, expectedDate, expectedDescription)]);
    }

    // A sold policy, then a claim whose body breaks the shape: 400 naming the key, and nothing recorded.
    // errorKey null: malformed JSON, whose errors entry the JSON reader names.
    private async Task AssertClaimShape(string reference, string json, string? errorKey)
    {
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(TestClock.Today).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostClaim(reference, json);

        var errors = await ProblemAssert.ValidationErrors(response, PolicyActions.ClaimsPath(reference));
        if (errorKey is not null)
        {
            ProblemAssert.HasError(errors, errorKey);
        }

        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }
}
