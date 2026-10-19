using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-1/2: a policy can be renewed from 30 days before the latest term's end up to the end date itself,
// inclusive, measured on UK today; otherwise 422 /problems/renew-1-2 with windowStart and windowEnd. RENEW-1 is
// the opening edge (end - 31 rejected, end - 30 accepted), RENEW-2 the closing edge (the end date accepted, the
// day after rejected). Each side of each edge is its own test. Each policy is sold (E1) with the clock at
// TestClock.Now, the clock is moved, and the renewal (E6) is read back whole (E2):
//   start 2026-10-29 ends 2027-10-28 (365 days); window 2027-09-28 to 2027-10-28;
//   the renewed term runs 2027-10-29 to 2028-10-28 (366 days, through 29 Feb 2028).
// A rejected renewal leaves the policy as sold: one term, no claim, refund or cancellation.
public sealed class RenewWindowTests : IClassFixture<PoliciesApiFactory>
{
    private const string RuleId = "RENEW-1/2";
    private const string Title = "Outside the renewal window";

    private static readonly DateOnly Start = new(2026, 10, 29);
    private static readonly DateOnly End = new(2027, 10, 28);
    private static readonly DateOnly WindowStart = new(2027, 9, 28);
    private static readonly DateOnly RenewedStart = new(2027, 10, 29);
    private static readonly DateOnly RenewedEnd = new(2028, 10, 28);

    private readonly PoliciesApiFactory _factory;

    public RenewWindowTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_31_days_before_the_end_is_rejected_with_422()
    {
        const string reference = "RENEW1-END-MINUS-31";
        var sold = Sale(reference, 431.62m, "DirectDebit");
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 9, 27));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(452.19m).Build());

        await AssertOutsideTheWindow(response, reference, WindowStart, End);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, End, "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_30_days_before_the_end_is_accepted()
    {
        const string reference = "RENEW1-END-MINUS-30";
        var sold = Sale(reference, 418.07m, "Card");
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 9, 28));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(446.83m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(Start, End, "418.07", "Card"),
                new ExpectedPolicyTerm(RenewedStart, RenewedEnd, "446.83", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    // UK today at 2027-09-27T23:30Z is 2027-09-28 (BST, UTC+1): the first day of the window, not the day before.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_at_23_30_UTC_in_BST_on_the_day_before_the_window_is_on_its_first_UK_day_and_accepted()
    {
        const string reference = "RENEW1-BST-START";
        var sold = Sale(reference, 437.75m, "DirectDebit");
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 9, 27, 23, 30, 0, TimeSpan.Zero));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(459.12m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(Start, End, "437.75", "DirectDebit"),
                new ExpectedPolicyTerm(RenewedStart, RenewedEnd, "459.12", "DirectDebit"),
            ]),
            [sale.PaymentReference()]);
    }

    // The policy is sold today, so its window opens eleven months away (2027-09-16 to 2027-10-16). This is the
    // row Postman runs in real time.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_on_the_day_of_sale_is_rejected_with_422()
    {
        const string reference = "RENEW1-SALE-DAY";
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(TestClock.Today).WithAmount(384.29m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(401.16m).Build());

        await AssertOutsideTheWindow(response, reference, new DateOnly(2027, 9, 16), new DateOnly(2027, 10, 16));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_on_the_end_date_is_accepted()
    {
        const string reference = "RENEW2-END-DATE";
        var sold = Sale(reference, 399.54m, "DirectDebit");
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(End);

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(421.30m).WithPaymentMethod("Card").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(Start, End, "399.54", "DirectDebit"),
                new ExpectedPolicyTerm(RenewedStart, RenewedEnd, "421.30", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    // The day after the end the policy has expired and can no longer be renewed.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_the_day_after_the_end_is_rejected_with_422()
    {
        const string reference = "RENEW2-END-PLUS-1";
        var sold = Sale(reference, 405.26m, "Card");
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 29));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(427.91m).Build());

        await AssertOutsideTheWindow(response, reference, WindowStart, End);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, End, "Expired");
    }

    // UK today at 2027-10-28T23:30Z is 2027-10-29 (BST, UTC+1): the day after the end, although the UTC date is the end.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_at_23_30_UTC_in_BST_on_the_end_date_is_on_the_next_UK_day_and_rejected()
    {
        const string reference = "RENEW2-BST-END";
        var sold = Sale(reference, 426.48m, "Card");
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 10, 28, 23, 30, 0, TimeSpan.Zero));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(448.02m).Build());

        await AssertOutsideTheWindow(response, reference, WindowStart, End);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, End, "Expired");
    }

    // One request, one UK today (finding A1/R1). The clock moves an hour forward on every read, from
    // 2027-10-28T22:30Z (23:30 BST, UK 2027-10-28, the end date); a second read would be 23:30Z (00:30 BST,
    // UK 2027-10-29). Read once, the renewal is accepted on the end date (RENEW-2) and the 201 body answers on
    // that same date: term 1 (2026-10-29 to 2027-10-28) is still the term in force, so the policy is Active
    // with currentTermNumber 1. Read twice, the body is built on 2027-10-29 and names term 2 as current,
    // although the window check saw 2027-10-28. The read-back is on 2027-10-28 too, at noon.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_read_across_UK_midnight_is_checked_and_answered_on_one_UK_today()
    {
        const string reference = "RENEW2-ONE-TODAY";
        var sold = Sale(reference, 413.58m, "Card");
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        var onTheEndDate = new ExpectedRenewedPolicy(reference, "Active", 1, [
            new ExpectedPolicyTerm(Start, End, "413.58", "Card"),
            new ExpectedPolicyTerm(RenewedStart, RenewedEnd, "438.27", "Card"),
        ]);

        HttpResponseMessage response;
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 10, 28, 22, 30, 0, TimeSpan.Zero));
        _factory.Clock.AutoAdvanceAmount = TimeSpan.FromHours(1);
        try
        {
            response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(438.27m).Build());
        }
        finally
        {
            _factory.Clock.AutoAdvanceAmount = TimeSpan.Zero;
        }

        IReadOnlyList<string?> paymentReferences;
        using (response)
        {
            var renewed = await RenewalActions.RenewedBody(response, reference);
            paymentReferences = RenewedPolicyAssert.ReadsBack(renewed, sold, onTheEndDate, [sale.PaymentReference()]);
        }

        _factory.Clock.AdjustTime(new DateTimeOffset(2027, 10, 28, 12, 0, 0, TimeSpan.Zero));
        using var read = await client.GetPolicy(reference);
        Assert.Equal(System.Net.HttpStatusCode.OK, read.StatusCode);
        _ = RenewedPolicyAssert.ReadsBack(await read.ReadJson(), sold, onTheEndDate, paymentReferences);
    }

    private static JsonObject Sale(string reference, decimal amount, string paymentMethod) =>
        new SellPolicyRequestBuilder(reference).WithStartDate(Start).WithAmount(amount).WithPaymentMethod(paymentMethod).Build();

    private static async Task AssertOutsideTheWindow(HttpResponseMessage response, string reference, DateOnly windowStart, DateOnly windowEnd)
    {
        var problem = await ProblemAssert.SingleRuleViolation(response, RuleId, Title, RenewalActions.RenewalsPath(reference));
        Assert.Equal(windowStart, problem.GetDate("windowStart"));
        Assert.Equal(windowEnd, problem.GetDate("windowEnd"));
    }
}
