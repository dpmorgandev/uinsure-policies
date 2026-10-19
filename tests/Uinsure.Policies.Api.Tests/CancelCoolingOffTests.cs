using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-1b: from the start date to start + 14 inclusive (the 14-day cooling-off, counted from the day after the
// start) the refund is in full. The day counts are still the calendar facts: the start date is day 1 used. The
// other side of the boundary, start + 15, is CANCEL-1c's (CancelProRataTests). Each test sells its own policy
// (POST → GET), cancels it through E3 and reads it back whole; the refund reverses the sold payment (CANCEL-2).
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days).
public sealed class CancelCoolingOffTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public CancelCoolingOffTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2026-10-17 is both the start date and today (CANCEL-3 allows today): 1 day used, 289.40 back to the Cheque.
    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    [Trait("Rule", "CANCEL-2")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_on_the_start_date_refunds_in_full_to_the_Cheque_paid()
    {
        const string reference = "CAN1B-START";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(289.40m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(TestClock.Today).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2027, 10, 16), paymentReference,
            TestClock.Today, "CANCEL-1b", 365, 1, 364, "289.40", "Cheque");
    }

    // 2026-11-22 is start + 14, the last day of cooling-off: 15 days used, 604.25 still back in full.
    [Fact]
    [Trait("Rule", "CANCEL-1b")]
    [Trait("Rule", "CANCEL-2")]
    public async Task Cancel_on_start_plus_14_refunds_in_full_to_the_Direct_Debit_paid()
    {
        const string reference = "CAN1B-PLUS-14";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 8)).WithAmount(604.25m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 11, 22)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2027, 11, 7), paymentReference,
            new DateOnly(2026, 11, 22), "CANCEL-1b", 365, 15, 350, "604.25", "DirectDebit");
    }
}
