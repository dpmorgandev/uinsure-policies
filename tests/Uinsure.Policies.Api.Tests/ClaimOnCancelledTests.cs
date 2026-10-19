using System.Text.Json;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-2c: no claim on a cancelled policy: 409 /problems/could-2c, giving the cancellation date (extension
// cancellationDate, and named in the detail). A 409 short-circuits, so it is returned before the 422 date rules.
// The policy reads back cancelled as it was, with no claim and hasClaims still false.
//   start 2026-10-17 ends 2027-10-16 (365 days).
public sealed class ClaimOnCancelledTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Claim on a cancelled policy";

    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public ClaimOnCancelledTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Cancelled with effect from 2026-12-15 (60 days used, 305 unused; 433.25 × 305 ÷ 365 = 362.03082… → 362.03),
    // then a claim dated today.
    [Fact]
    [Trait("Rule", "COULD-2c")]
    public async Task Claim_on_a_cancelled_policy_answers_409_with_the_cancellation_date()
    {
        const string reference = "CLAIM-CANCELLED";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(433.25m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        var refundReference = RefundReference(await client.Cancel(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 12, 15)).Build()));

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().Build());

        var problem = await ProblemAssert.Conflict(response, "COULD-2c", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(new DateOnly(2026, 12, 15), problem.GetDate("cancellationDate"));
        Assert.Contains("2026-12-15", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Cancelled", false,
            new ExpectedCancellationSummary(new DateOnly(2026, 12, 15), "CANCEL-1c", "362.03"),
            [new ExpectedPolicyRefund(refundReference, "DirectDebit", "362.03", paymentReference)],
            []);
    }

    // Cancelled today (2026-10-17, the start date: CANCEL-1b, 309.48 in full), then a claim dated tomorrow,
    // which would be a 422 COULD-2a on its own: the 409 wins.
    [Fact]
    [Trait("Rule", "COULD-2c")]
    public async Task Claim_with_a_future_date_on_a_cancelled_policy_answers_409_before_the_date_rules()
    {
        const string reference = "CLAIM-CANC-FUTURE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(309.48m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        using var cancelled = await client.PostCancellationWithNoBody(reference);
        var refundReference = RefundReference(await PolicyActions.CancelledBody(cancelled));

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().WithClaimDate(TestClock.Today.AddDays(1)).Build());

        var problem = await ProblemAssert.Conflict(response, "COULD-2c", Title, PolicyActions.ClaimsPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("cancellationDate"));
        Assert.Contains("2026-10-17", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Cancelled", false,
            new ExpectedCancellationSummary(TestClock.Today, "CANCEL-1b", "309.48"),
            [new ExpectedPolicyRefund(refundReference, "Card", "309.48", paymentReference)],
            []);
    }

    private static string RefundReference(JsonElement cancellation) =>
        Assert.Single(cancellation.Member("refunds").EnumerateArray()).Member("reference").GetString() ?? string.Empty;
}
