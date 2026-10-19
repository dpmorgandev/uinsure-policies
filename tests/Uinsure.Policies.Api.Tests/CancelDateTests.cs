using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-3, the cancellation date (E3): optional, defaulting to UK today (no body, {} and a null date all mean
// today), and never in the past (422, with today as an extension). A rejected cancellation leaves the policy as
// sold. Each default row moves the clock to a UK today of its own, so the date the app fills in can't be a
// canned one. Refunds and day counts are hand-worked literals:
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days);
//   start 2027-06-01 ends 2028-05-31 (366 days, sold with the clock at 2027-06-01).
public sealed class CancelDateTests : IClassFixture<PoliciesApiFactory>
{
    private const string PastTitle = "Cancellation date in the past";

    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);
    private static readonly DateOnly FutureStart = new(2026, 11, 8);
    private static readonly DateOnly FutureStartEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public CancelDateTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // No body on UK today 2026-11-30 (start + 22): 23 days used, 342 unused; 280.36 × 342 ÷ 365 = 262.69347… → 262.69.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_no_body_means_UK_today()
    {
        const string reference = "CAN3-NO-BODY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(280.36m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 30, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostCancellationWithNoBody(reference);

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, FutureStartEnd, paymentReference,
            new DateOnly(2026, 11, 30), "CANCEL-1c", 365, 23, 342, "262.69", "Card");
    }

    // {} on UK today 2026-11-20 (start + 34): 35 days used, 330 unused; 412.66 × 330 ÷ 365 = 373.08986… → 373.09.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_an_empty_object_means_UK_today()
    {
        const string reference = "CAN3-EMPTY-OBJECT";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(412.66m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithoutDate().Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, TodayStartEnd, paymentReference,
            new DateOnly(2026, 11, 20), "CANCEL-1c", 365, 35, 330, "373.09", "Cheque");
    }

    // A null date on UK today 2026-11-12 (start + 4, cooling-off): 5 days used, 360 unused, 250.50 back in full.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_a_null_date_means_UK_today()
    {
        const string reference = "CAN3-NULL-DATE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(250.50m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(null).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, FutureStartEnd, paymentReference,
            new DateOnly(2026, 11, 12), "CANCEL-1b", 365, 5, 360, "250.50", "DirectDebit");
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_yesterday_is_rejected_as_past()
    {
        const string reference = "CAN3-YESTERDAY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(333.80m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(TestClock.Today.AddDays(-1)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-3", PastTitle, PolicyActions.CancellationPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("today"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }

    // 2027-06-30T23:30Z is 00:30 BST on 2027-07-01, so UK today is 2027-07-01 and the UTC date is in the past.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past()
    {
        const string reference = "CAN3-BST-UTC";
        var sold = await SellOnJuneFirst2027(reference, 298.15m);
        using var client = _factory.CreateClient();
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 30, 23, 30, 0, TimeSpan.Zero));

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 6, 30)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-3", PastTitle, PolicyActions.CancellationPath(reference));
        Assert.Equal(new DateOnly(2027, 7, 1), problem.GetDate("today"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2028, 5, 31), "Active");
    }

    // The control: at the same instant the UK date 2027-07-01 is today. In the 366-day term 2027-06-01 to
    // 2028-05-31 that is 31 days used, 335 unused; 298.15 × 335 ÷ 366 = 272.89685… → 272.90.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted_as_today()
    {
        const string reference = "CAN3-BST-UK";
        var sold = await SellOnJuneFirst2027(reference, 298.15m);
        using var client = _factory.CreateClient();
        var paymentReference = (await client.ReadBack(reference)).PaymentReference();
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 30, 23, 30, 0, TimeSpan.Zero));

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 7, 1)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2028, 5, 31), paymentReference,
            new DateOnly(2027, 7, 1), "CANCEL-1c", 366, 31, 335, "272.90", "DirectDebit");
    }

    // UK today 2027-11-10: 2027-11-01 is both in the past and after the 2027-10-16 end. Both rules are reported,
    // CANCEL-3 first (rule-ID order), and the policy, now Expired, is left as sold.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    [Trait("Rule", "CANCEL-7")]
    public async Task Past_date_after_the_end_of_cover_reports_CANCEL_3_and_CANCEL_7()
    {
        const string reference = "CAN3-CAN7-BOTH";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(299.99m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 11, 10, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 11, 1)).Build());

        await ProblemAssert.MultipleRuleViolations(response, PastTitle, PolicyActions.CancellationPath(reference), "CANCEL-3", "CANCEL-7");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Expired");
    }

    // Sold with the clock at 2027-06-01 to start that day; ends 2028-05-31. Returns the sale.
    private async Task<JsonObject> SellOnJuneFirst2027(string reference, decimal amount)
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2027, 6, 1)).WithAmount(amount).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        return sold;
    }
}
