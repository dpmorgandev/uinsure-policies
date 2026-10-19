using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-6: a refund is rounded once, to 2 dp, half away from zero (.NET's default is banker's rounding, half to
// even). Only a 366-day term can put a pro-rata refund exactly on half a penny (amount × unused ÷ 365 never
// ends in .xx5), so the policy starts 2027-03-01 and ends 2028-02-29, sold with the clock at 2027-02-01. Money is
// asserted as the raw JSON text: an unrounded 50.005 would show as itself, a banker's 50.00 as 50.00. The quote
// side of the row is CancellationQuoteTests; the rows whose refund has more than 2 dp before rounding are
// CancelProRataTests and CancellationQuoteTests.
public sealed class CancelRoundingTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public CancelRoundingTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2027-08-30: 183 days used, 183 unused; 100.01 × 183 ÷ 366 = 50.005 exactly → 50.01.
    [Fact]
    [Trait("Rule", "CANCEL-6")]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Refund_of_exactly_half_a_penny_rounds_away_from_zero()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero));
        const string reference = "CAN6-HALF-PENNY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2027, 3, 1)).WithAmount(100.01m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 8, 30)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, new DateOnly(2028, 2, 29), paymentReference,
            new DateOnly(2027, 8, 30), "CANCEL-1c", 366, 183, 183, "50.01", "Card");
    }
}
