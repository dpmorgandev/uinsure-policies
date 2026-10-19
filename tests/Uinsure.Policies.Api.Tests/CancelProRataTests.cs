using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-1c: from start + 15, after cooling-off, the refund is amount × unusedDays ÷ daysInTerm, over the term's
// actual days (365 or 366), with the cancellation day counted as used. Each test sells its own policy
// (POST → GET), cancels it through E3 and reads it back whole: Cancelled overrides the derived status, and the
// cancellation and the refund are stored. Each refund is a literal worked out by hand from the sale:
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days);
//   start 2027-03-01 ends 2028-02-29 (366 days, sold with the clock at 2027-02-01).
public sealed class CancelProRataTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public CancelProRataTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2026-11-23 is start + 15, the first day after cooling-off: 16 days used, 349 unused.
    // 527.18 × 349 ÷ 365 = 504.0707… → 504.07 (more than 2 dp before rounding, CANCEL-6).
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    [Trait("Rule", "CANCEL-6")]
    public async Task Cancel_on_start_plus_15_refunds_the_unused_days()
    {
        const string reference = "CAN1C-PLUS-15";
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 11, 23)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2027, 11, 7), paymentReference,
            new DateOnly(2026, 11, 23), "CANCEL-1c", 365, 16, 349, "504.07", "DirectDebit");
    }

    // 2027-03-04 in the 365-day term 2026-10-17 to 2027-10-16: 139 days used, 226 unused.
    // 730.00 × 226 ÷ 365 = 452.00.
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Cancel_mid_term_of_a_365_day_term_refunds_the_unused_days_over_365()
    {
        const string reference = "CAN1C-365";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(730.00m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 3, 4)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2027, 10, 16), paymentReference,
            new DateOnly(2027, 3, 4), "CANCEL-1c", 365, 139, 226, "452.00", "Card");
    }

    // 2027-06-01 in the 366-day term 2027-03-01 to 2028-02-29: 93 days used, 273 unused.
    // 732.00 × 273 ÷ 366 = 546.00 (a 365-day count would leave 272 unused and give 545.49).
    [Fact]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Cancel_mid_term_of_a_366_day_term_refunds_the_unused_days_over_366()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero));
        const string reference = "CAN1C-366";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2027, 3, 1)).WithAmount(732.00m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 6, 1)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2028, 2, 29), paymentReference,
            new DateOnly(2027, 6, 1), "CANCEL-1c", 366, 93, 273, "546.00", "DirectDebit");
    }
}
