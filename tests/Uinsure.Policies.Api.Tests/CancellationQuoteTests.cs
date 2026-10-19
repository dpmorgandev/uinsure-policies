using System.Net;
using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-1: E4 GET /policies/{reference}/cancellation-quote?cancellationDate= is the cancel calculation, read
// only. It answers 200 with the E3 shape plus quotedOn (UK today); its refunds carry no reference, because
// nothing is created; and the policy reads back unchanged afterwards. The quote and a later cancel for the same
// date agree. The date rules (CANCEL-3, CANCEL-7) apply to a quote too. Refunds and day counts are
// hand-worked literals:
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days);
//   start 2027-03-01 ends 2028-02-29 (366 days, sold with the clock at 2027-02-01).
public sealed class CancellationQuoteTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);
    private static readonly DateOnly FutureStart = new(2026, 11, 8);
    private static readonly DateOnly FutureStartEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public CancellationQuoteTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2027-04-12: 178 days used, 187 unused; 615.40 × 187 ÷ 365 = 315.28712… → 315.29, by Direct Debit.
    // The quote, then the cancel for the same date: the same rule, day counts and refund, each a literal.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Quote_and_a_later_cancel_for_the_same_date_agree()
    {
        const string reference = "QUOTE-AGREE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(615.40m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        var cancellationDate = new DateOnly(2027, 4, 12);
        var expected = new ExpectedCancellation(
            reference, cancellationDate, "CANCEL-1c", "315.29",
            [new ExpectedTerm(1, "CANCEL-1c", 365, 178, 187, "315.29")],
            [new ExpectedRefund("DirectDebit", "315.29", paymentReference)]);

        var quote = await client.Quote(reference, cancellationDate);
        var cancellation = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancellationDate).Build());

        CancellationAssert.IsQuote(quote, expected, TestClock.Today);
        var refundReferences = CancellationAssert.IsCancellation(cancellation, expected);
        Assert.Equal(cancellation.Member("terms").GetRawText(), quote.Member("terms").GetRawText());
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Cancelled", false,
            new ExpectedCancellationSummary(cancellationDate, "CANCEL-1c", "315.29"),
            [new ExpectedPolicyRefund(Assert.Single(refundReferences), "DirectDebit", "315.29", paymentReference)],
            []);
    }

    // 2026-11-30 (start + 22): 23 days used, 342 unused; 384.26 × 342 ÷ 365 = 360.04635… → 360.05. The policy
    // reads back byte for byte as before the quote, and a cancel afterwards is not refused as a second one.
    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_changes_nothing_on_the_policy()
    {
        const string reference = "QUOTE-NO-CHANGE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(384.26m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        var before = await ReadBackText(client, reference);

        var quote = await client.Quote(reference, new DateOnly(2026, 11, 30));

        CancellationAssert.IsQuote(quote, new ExpectedCancellation(
            reference, new DateOnly(2026, 11, 30), "CANCEL-1c", "360.05",
            [new ExpectedTerm(1, "CANCEL-1c", 365, 23, 342, "360.05")],
            [new ExpectedRefund("Card", "360.05", paymentReference)]), TestClock.Today);
        Assert.Equal(before, await ReadBackText(client, reference));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, FutureStartEnd, "Pending");
        _ = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 11, 30)).Build());
    }

    // 2026-10-25 is before the 2026-11-08 start: nothing used, 291.35 in full, back to the Cheque.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-1a")]
    public async Task Quote_before_the_start_quotes_a_full_refund_under_CANCEL_1a()
    {
        await AssertQuote(
            "QUOTE-1A",
            new SellPolicyRequestBuilder("QUOTE-1A").WithStartDate(FutureStart).WithAmount(291.35m).WithPaymentMethod("Cheque").Build(),
            FutureStartEnd, "Pending", new DateOnly(2026, 10, 25), "CANCEL-1a", 365, 0, 365, "291.35", "Cheque");
    }

    // 2026-10-31 is start + 14, the last day of cooling-off: 15 days used, 503.60 in full.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-1b")]
    public async Task Quote_on_start_plus_14_quotes_a_full_refund_under_CANCEL_1b()
    {
        await AssertQuote(
            "QUOTE-1B-PLUS-14",
            new SellPolicyRequestBuilder("QUOTE-1B-PLUS-14").WithStartDate(TestClock.Today).WithAmount(503.60m).WithPaymentMethod("DirectDebit").Build(),
            TodayStartEnd, "Active", new DateOnly(2026, 10, 31), "CANCEL-1b", 365, 15, 350, "503.60", "DirectDebit");
    }

    // 2026-11-01 is start + 15: 16 days used, 349 unused; 687.45 × 349 ÷ 365 = 657.31520… → 657.32 (rounded up:
    // more than 2 dp before rounding, CANCEL-6).
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-1c")]
    [Trait("Rule", "CANCEL-6")]
    public async Task Quote_on_start_plus_15_quotes_the_unused_days_under_CANCEL_1c()
    {
        await AssertQuote(
            "QUOTE-1C-PLUS-15",
            new SellPolicyRequestBuilder("QUOTE-1C-PLUS-15").WithStartDate(TestClock.Today).WithAmount(687.45m).WithPaymentMethod("Card").Build(),
            TodayStartEnd, "Active", new DateOnly(2026, 11, 1), "CANCEL-1c", 365, 16, 349, "657.32", "Card");
    }

    // UK today 2027-02-01. 2027-12-30 in the 366-day term 2027-03-01 to 2028-02-29: 305 days used, 61 unused.
    // 120.03 × 61 ÷ 366 = 20.005 exactly: away from zero 20.01, where banker's rounding would give 20.00.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-6")]
    public async Task Quote_of_exactly_half_a_penny_rounds_away_from_zero()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero));
        await AssertQuote(
            "QUOTE-HALF-PENNY",
            new SellPolicyRequestBuilder("QUOTE-HALF-PENNY").WithStartDate(new DateOnly(2027, 3, 1)).WithAmount(120.03m).WithPaymentMethod("DirectDebit").Build(),
            new DateOnly(2028, 2, 29), "Pending", new DateOnly(2027, 12, 30), "CANCEL-1c", 366, 305, 61, "20.01", "DirectDebit",
            quotedOn: new DateOnly(2027, 2, 1));
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Quote_for_a_past_date_is_rejected()
    {
        const string reference = "QUOTE-PAST";
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(TestClock.Today).WithAmount(341.20m).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.GetQuote(reference, TestClock.Today.AddDays(-1));

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-3", "Cancellation date in the past", PolicyActions.QuotePath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("today"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }

    // One request, one UK today (finding A2/R1). The clock moves an hour forward on every read, from
    // 2026-10-17T22:30Z (23:30 BST, UK 2026-10-17); a second read would be 23:30Z (00:30 BST, UK 2026-10-18).
    // A quote for UK 2026-10-17 read once is 200 quoted on 2026-10-17: the date CANCEL-3 checks against is the
    // quotedOn it answers with. Read twice, the past-date check sees 2026-10-18 and the quote is a 422.
    // 2026-10-17 is the start date: 1 day used, 364 unused, 452.76 in full to the Card (CANCEL-1b).
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Quote_read_across_UK_midnight_is_checked_and_quoted_on_one_UK_today()
    {
        const string reference = "QUOTE-ONE-TODAY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(452.76m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        HttpResponseMessage response;
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 17, 22, 30, 0, TimeSpan.Zero));
        _factory.Clock.AutoAdvanceAmount = TimeSpan.FromHours(1);
        try
        {
            response = await client.GetQuote(reference, new DateOnly(2026, 10, 17));
        }
        finally
        {
            _factory.Clock.AutoAdvanceAmount = TimeSpan.Zero;
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            CancellationAssert.IsQuote(await response.ReadJson(), new ExpectedCancellation(
                reference, new DateOnly(2026, 10, 17), "CANCEL-1b", "452.76",
                [new ExpectedTerm(1, "CANCEL-1b", 365, 1, 364, "452.76")],
                [new ExpectedRefund("Card", "452.76", paymentReference)]), new DateOnly(2026, 10, 17));
        }
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-7")]
    public async Task Quote_for_the_day_after_the_end_of_cover_is_rejected()
    {
        const string reference = "QUOTE-AFTER-END";
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(FutureStart).WithAmount(296.85m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.GetQuote(reference, new DateOnly(2027, 11, 8));

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-7", "Cancellation date after the end of cover", PolicyActions.QuotePath(reference));
        Assert.Equal(FutureStartEnd, problem.GetDate("latestEndDate"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, FutureStartEnd, "Pending");
    }

    // A claim on 2026-10-17, then a quote for 2026-10-20 (cooling-off): COULD-2, nothing to refund, no refund.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "COULD-2")]
    public async Task Quote_for_a_policy_with_a_claim_quotes_nothing_under_COULD_2()
    {
        const string reference = "QUOTE-WITH-CLAIM";
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(TestClock.Today).WithAmount(476.25m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        var claimReference = ClaimAssert.IsClaim(
            await client.RecordClaim(reference, new ClaimRequestBuilder().WithDescription("Cracked bathroom basin").Build()),
            reference, TestClock.Today, "Cracked bathroom basin");

        var quote = await client.Quote(reference, new DateOnly(2026, 10, 20));

        CancellationAssert.IsQuote(quote, new ExpectedCancellation(
            reference, new DateOnly(2026, 10, 20), "COULD-2", "0.00",
            [new ExpectedTerm(1, "COULD-2", 365, 4, 361, "0.00")],
            []), TestClock.Today);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active", true, null, [],
            [new ExpectedClaim(claimReference, TestClock.Today, "Cracked bathroom basin")]);
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_without_a_cancellationDate_is_rejected_with_400()
    {
        await AssertQuoteShape("QUOTE-NO-DATE", string.Empty, "cancellationDate");
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_with_a_cancellationDate_not_in_yyyy_MM_dd_is_rejected_with_400()
    {
        await AssertQuoteShape("QUOTE-BAD-FORMAT", "cancellationDate=11/30/2026", "cancellationDate");
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_with_a_cancellationDate_that_is_not_a_calendar_date_is_rejected_with_400()
    {
        await AssertQuoteShape("QUOTE-NOT-A-DATE", "cancellationDate=2027-02-30", "cancellationDate");
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_with_an_unknown_query_parameter_is_rejected_with_400()
    {
        await AssertQuoteShape("QUOTE-TYPO", "cancellationDate=2026-11-30&cancelationDate=2026-11-30", "cancelationDate");
    }

    // reference is bound from the route, never the query, so as a query key it is unknown (finding F2 A2/I4).
    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_with_a_reference_query_parameter_is_rejected_with_400()
    {
        await AssertQuoteShape("QUOTE-REF-KEY", "cancellationDate=2026-11-30&reference=OTHER-REF", "reference");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Quote_for_an_unknown_reference_answers_404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetQuote("QUOTE-NO-SUCH", new DateOnly(2026, 11, 30));

        await ProblemAssert.NotFound(response, PolicyActions.QuotePath("QUOTE-NO-SUCH"), "QUOTE-NO-SUCH");
    }

    private async Task AssertQuote(
        string reference,
        JsonObject sold,
        DateOnly expectedEnd,
        string expectedStatus,
        DateOnly cancellationDate,
        string expectedRule,
        int daysInTerm,
        int daysUsed,
        int unusedDays,
        string refund,
        string refundType,
        DateOnly? quotedOn = null)
    {
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        var quote = await client.Quote(reference, cancellationDate);

        CancellationAssert.IsQuote(quote, new ExpectedCancellation(
            reference, cancellationDate, expectedRule, refund,
            [new ExpectedTerm(1, expectedRule, daysInTerm, daysUsed, unusedDays, refund)],
            [new ExpectedRefund(refundType, refund, paymentReference)]), quotedOn ?? TestClock.Today);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, expectedEnd, expectedStatus);
    }

    // A sold policy, then a quote whose query is malformed: 400 with the key in errors, and nothing changed.
    private async Task AssertQuoteShape(string reference, string query, string errorKey)
    {
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.GetQuote(reference, query);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response, PolicyActions.QuotePath(reference)), errorKey);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, FutureStartEnd, "Pending");
    }

    private static async Task<string> ReadBackText(HttpClient client, string reference)
    {
        using var read = await client.GetPolicy(reference);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        return await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
