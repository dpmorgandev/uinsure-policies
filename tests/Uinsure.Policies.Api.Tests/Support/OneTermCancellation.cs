using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// A one-term policy's cancellation, asserted end to end: the E3 201 body (the rule applied, the day counts and
// the refund, each the test's literal) and then the E2 read-back (Cancelled, with the cancellation and the
// refund stored, the refund reversing the sold payment). refundType null means a £0 refund: no Refund record,
// so refunds[] is empty in both.
internal static class OneTermCancellation
{
    public static async Task AssertCancelledAndReadBack(
        HttpClient client,
        HttpResponseMessage response,
        string reference,
        JsonObject sold,
        DateOnly expectedEnd,
        string paymentReference,
        DateOnly expectedDate,
        string expectedRule,
        int daysInTerm,
        int daysUsed,
        int unusedDays,
        string refund,
        string? refundType)
    {
        var body = await PolicyActions.CancelledBody(response);

        ExpectedRefund[] refunds = refundType is null ? [] : [new(refundType, refund, paymentReference)];
        var refundReferences = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, expectedDate, expectedRule, refund,
            [new ExpectedTerm(1, expectedRule, daysInTerm, daysUsed, unusedDays, refund)],
            refunds));

        ExpectedPolicyRefund[] stored = refundType is null ? [] : [new(Assert.Single(refundReferences), refundType, refund, paymentReference)];
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, expectedEnd, "Cancelled", false,
            new ExpectedCancellationSummary(expectedDate, expectedRule, refund), stored, []);
    }
}
