using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-1a: cancelling before the start date refunds the amount in full. Nothing of the term is used
// (daysUsed 0, unusedDays = daysInTerm). Each test sells its own policy (POST → GET), cancels it through E3 and
// reads it back whole: Cancelled overrides the Pending it would otherwise show, and the cancellation and the
// refund are stored. The refund reverses the sold payment, by its method (CANCEL-2).
//   start 2026-11-08 ends 2027-11-07 (365 days); TestClock.Today is 2026-10-17.
public sealed class CancelBeforeStartTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly FutureStart = new(2026, 11, 8);
    private static readonly DateOnly FutureStartEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public CancelBeforeStartTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2026-11-07, the day before the start: 318.62 back in full, to the Card that paid it.
    [Fact]
    [Trait("Rule", "CANCEL-1a")]
    [Trait("Rule", "CANCEL-2")]
    public async Task Cancel_on_the_day_before_the_start_refunds_in_full_to_the_Card_paid()
    {
        const string reference = "CAN1A-DAY-BEFORE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(FutureStart).WithAmount(318.62m).WithPaymentMethod("Card").WithAutoRenew(false).Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2026, 11, 7)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, FutureStartEnd, paymentReference,
            new DateOnly(2026, 11, 7), "CANCEL-1a", 365, 0, 365, "318.62", "Card");
    }

    // No body at all, so today (2026-10-17, CANCEL-3), 22 days before the start: 527.18 back by Direct Debit.
    [Fact]
    [Trait("Rule", "CANCEL-1a")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_no_body_before_the_start_cancels_today_with_a_full_refund()
    {
        const string reference = "CAN1A-NO-BODY";
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellationWithNoBody(reference);

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, FutureStartEnd, paymentReference,
            TestClock.Today, "CANCEL-1a", 365, 0, 365, "527.18", "DirectDebit");
    }
}
