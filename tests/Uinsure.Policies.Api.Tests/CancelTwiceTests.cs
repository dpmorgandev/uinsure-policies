using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-4: a cancelled policy can't be cancelled again, or quoted. 409 /problems/cancel-4, giving the original
// cancellation date (extension cancellationDate, and named in the detail). A 409 short-circuits, so it is
// returned before any 422 date rule. The first cancellation stays exactly as it was stored.
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days).
public sealed class CancelTwiceTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Policy already cancelled";

    private readonly PoliciesApiFactory _factory;

    public CancelTwiceTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // First cancellation 2027-01-20: 96 days used, 269 unused; 458.12 × 269 ÷ 365 = 337.62816… → 337.63.
    [Fact]
    [Trait("Rule", "CANCEL-4")]
    public async Task Cancelling_a_cancelled_policy_answers_409_with_the_original_cancellation_date()
    {
        const string reference = "CAN4-TWICE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(458.12m).WithPaymentMethod("Card").Build();
        var first = await SellAndCancel(reference, sold, new DateOnly(2027, 1, 20));
        using var client = _factory.CreateClient();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 2, 14)).Build());

        var problem = await ProblemAssert.Conflict(response, "CANCEL-4", Title, PolicyActions.CancellationPath(reference));
        Assert.Equal(new DateOnly(2027, 1, 20), problem.GetDate("cancellationDate"));
        Assert.Contains("2027-01-20", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Cancelled", false,
            new ExpectedCancellationSummary(new DateOnly(2027, 1, 20), "CANCEL-1c", "337.63"),
            [new ExpectedPolicyRefund(first.RefundReference, "Card", "337.63", first.PaymentReference)],
            []);
    }

    // The second request also carries a past date (a 422 CANCEL-3 on its own): the 409 wins. The first
    // cancellation was today, 2026-10-17, before the 2026-11-08 start: CANCEL-1a, 512.00 back by Direct Debit.
    [Fact]
    [Trait("Rule", "CANCEL-4")]
    public async Task Cancelling_twice_with_a_past_date_answers_409_before_the_date_rule()
    {
        const string reference = "CAN4-TWICE-PAST";
        var sold = new SellPolicyRequestBuilder(reference).WithAmount(512.00m).Build();
        var first = await SellAndCancel(reference, sold, TestClock.Today);
        using var client = _factory.CreateClient();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 10, 1)).Build());

        var problem = await ProblemAssert.Conflict(response, "CANCEL-4", Title, PolicyActions.CancellationPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("cancellationDate"));
        Assert.Contains("2026-10-17", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 7), "Cancelled", false,
            new ExpectedCancellationSummary(TestClock.Today, "CANCEL-1a", "512.00"),
            [new ExpectedPolicyRefund(first.RefundReference, "DirectDebit", "512.00", first.PaymentReference)],
            []);
    }

    // E4 on a cancelled policy: there is nothing left to quote. First cancellation 2026-12-01: 46 days used,
    // 319 unused; 318.90 × 319 ÷ 365 = 278.70986… → 278.71, back to the Cheque.
    [Fact]
    [Trait("Rule", "CANCEL-4")]
    [Trait("Rule", "COULD-1")]
    public async Task Quoting_a_cancelled_policy_answers_409_with_the_original_cancellation_date()
    {
        const string reference = "CAN4-QUOTE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(318.90m).WithPaymentMethod("Cheque").Build();
        var first = await SellAndCancel(reference, sold, new DateOnly(2026, 12, 1));
        using var client = _factory.CreateClient();

        using var response = await client.GetQuote(reference, new DateOnly(2027, 1, 5));

        var problem = await ProblemAssert.Conflict(response, "CANCEL-4", Title, PolicyActions.QuotePath(reference));
        Assert.Equal(new DateOnly(2026, 12, 1), problem.GetDate("cancellationDate"));
        Assert.Contains("2026-12-01", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Cancelled", false,
            new ExpectedCancellationSummary(new DateOnly(2026, 12, 1), "CANCEL-1c", "278.71"),
            [new ExpectedPolicyRefund(first.RefundReference, "Cheque", "278.71", first.PaymentReference)],
            []);
    }

    // Sell and cancel once (201). Returns the payment and the refund references, for the read-back.
    private async Task<(string PaymentReference, string RefundReference)> SellAndCancel(string reference, JsonObject sold, DateOnly cancellationDate)
    {
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancellationDate).Build());
        var refund = Assert.Single(body.Member("refunds").EnumerateArray());
        return (paymentReference, refund.Member("reference").GetString() ?? string.Empty);
    }
}
