using System.Text.Json;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// One term's line of a cancellation or a quote (api.md E3): the rule applied and the day counts, with the
// refund as the exact JSON text (2 dp). Every value is the test's own literal, never derived here.
// Day counts: daysInTerm = end - start + 1 (365 or 366); daysUsed = the days of cover from the start up to and
// including the cancellation date (CANCEL-1c: the cancellation day counts as used), 0 before the start;
// unusedDays = daysInTerm - daysUsed. They are the same calendar facts whichever rule decides the refund.
internal sealed record ExpectedTerm(int TermNumber, string RuleApplied, int DaysInTerm, int DaysUsed, int UnusedDays, string RefundAmount);

// A refund the cancellation creates: the method and reference of the payment it reverses (CANCEL-2) and its
// amount as the exact JSON text.
internal sealed record ExpectedRefund(string Type, string Amount, string PaymentReference);

// The whole CancellationResponse (E3) or CancellationQuoteResponse (E4), less the generated refund references.
internal sealed record ExpectedCancellation(
    string Reference,
    DateOnly CancellationDate,
    string RuleApplied,
    string TotalRefund,
    IReadOnlyList<ExpectedTerm> Terms,
    IReadOnlyList<ExpectedRefund> Refunds);

// The E3 and E4 bodies, member for member. Money is compared as the raw JSON token, so a refund that missed
// Money.Round (CANCEL-6) shows as its unrounded text, and 504.1 can't pass for 504.10.
internal static class CancellationAssert
{
    private static readonly string[] CancellationMembers = ["reference", "cancellationDate", "ruleApplied", "totalRefund", "terms", "refunds"];
    private static readonly string[] QuoteMembers = [.. CancellationMembers, "quotedOn"];
    private static readonly string[] TermMembers = ["termNumber", "ruleApplied", "daysInTerm", "daysUsed", "unusedDays", "refundAmount"];
    private static readonly string[] RefundMembers = ["reference", "type", "amount", "paymentReference"];

    // A quote creates nothing, so its refunds carry no reference (api.md E4).
    private static readonly string[] QuoteRefundMembers = ["type", "amount", "paymentReference"];

    // E3. Returns the generated reference of each refund, in order, for the read-back to match.
    public static IReadOnlyList<string> IsCancellation(JsonElement body, ExpectedCancellation expected)
    {
        PolicyResponseAssert.HasMembers(body, CancellationMembers, "cancellation");
        HasTheValues(body, expected);

        var refunds = Refunds(body, expected, RefundMembers);
        var references = refunds.Select(refund => refund.Member("reference").GetString() ?? string.Empty).ToArray();
        for (var i = 0; i < references.Length; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(references[i]), $"Refund {i + 1} has no reference.");
            Assert.NotEqual(expected.Refunds[i].PaymentReference, references[i]);
        }

        return references;
    }

    // E4: the same calculation as E3 plus quotedOn (UK today), with nothing created.
    public static void IsQuote(JsonElement body, ExpectedCancellation expected, DateOnly quotedOn)
    {
        PolicyResponseAssert.HasMembers(body, QuoteMembers, "quote");
        HasTheValues(body, expected);
        Assert.Equal(quotedOn, body.GetDate("quotedOn"));
        _ = Refunds(body, expected, QuoteRefundMembers);
    }

    // The exact JSON token of a money member, for example "504.07".
    public static void Money(JsonElement element, string member, string expected) =>
        Assert.Equal(expected, element.Member(member).GetRawText());

    private static void HasTheValues(JsonElement body, ExpectedCancellation expected)
    {
        Assert.Equal(expected.Reference, body.Member("reference").GetString());
        Assert.Equal(expected.CancellationDate, body.GetDate("cancellationDate"));
        Assert.Equal(expected.RuleApplied, body.Member("ruleApplied").GetString());
        Money(body, "totalRefund", expected.TotalRefund);

        var terms = body.Member("terms").EnumerateArray().ToArray();
        Assert.Equal(expected.Terms.Count, terms.Length);
        for (var i = 0; i < terms.Length; i++)
        {
            var term = terms[i];
            var want = expected.Terms[i];
            PolicyResponseAssert.HasMembers(term, TermMembers, "cancellation term");
            Assert.Equal(want.TermNumber, term.Member("termNumber").GetInt32());
            Assert.Equal(want.RuleApplied, term.Member("ruleApplied").GetString());
            Assert.Equal(want.DaysInTerm, term.Member("daysInTerm").GetInt32());
            Assert.Equal(want.DaysUsed, term.Member("daysUsed").GetInt32());
            Assert.Equal(want.UnusedDays, term.Member("unusedDays").GetInt32());
            Money(term, "refundAmount", want.RefundAmount);
        }
    }

    private static JsonElement[] Refunds(JsonElement body, ExpectedCancellation expected, string[] members)
    {
        var refunds = body.Member("refunds").EnumerateArray().ToArray();
        Assert.Equal(expected.Refunds.Count, refunds.Length);
        for (var i = 0; i < refunds.Length; i++)
        {
            var refund = refunds[i];
            var want = expected.Refunds[i];
            PolicyResponseAssert.HasMembers(refund, members, "refund");
            Assert.Equal(want.Type, refund.Member("type").GetString());
            Money(refund, "amount", want.Amount);
            Assert.Equal(want.PaymentReference, refund.Member("paymentReference").GetString());
        }

        return refunds;
    }
}
