using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-7: the cancellation date must be on or before the latest term's end, else 422 with latestEndDate.
// With CANCEL-3 (today by default, never in the past) this blocks cancelling an expired policy. Each side of the
// end date is its own test; a rejected cancellation leaves the policy as sold.
//   start 2026-10-17 ends 2027-10-16; start 2026-11-08 ends 2027-11-07 (both 365 days).
public sealed class CancelAfterCoverTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Cancellation date after the end of cover";

    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public CancelAfterCoverTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2027-10-16, the last day of cover: all 365 days used, none unused, so the refund is 0.00 and no Refund
    // record is made (refunds[] empty).
    [Fact]
    [Trait("Rule", "CANCEL-7")]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Cancel_on_the_last_day_of_cover_is_accepted_and_refunds_nothing_with_no_refund_record()
    {
        const string reference = "CAN7-AT-END";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(365.73m).Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(TodayStartEnd).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, sold, TodayStartEnd, paymentReference,
            TodayStartEnd, "CANCEL-1c", 365, 365, 0, "0.00", refundType: null);
    }

    [Fact]
    [Trait("Rule", "CANCEL-7")]
    public async Task Cancellation_date_the_day_after_the_end_of_cover_is_rejected()
    {
        const string reference = "CAN7-END-PLUS-1";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(377.77m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 10, 17)).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-7", Title, PolicyActions.CancellationPath(reference));
        Assert.Equal(TodayStartEnd, problem.GetDate("latestEndDate"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, TodayStartEnd, "Active");
    }

    // UK today 2027-11-20 is after the 2027-11-07 end, so today (the default) is after cover: CANCEL-7 alone.
    [Fact]
    [Trait("Rule", "CANCEL-7")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancelling_an_expired_policy_today_is_rejected_as_after_the_end_of_cover()
    {
        const string reference = "CAN7-EXPIRED";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 8)).WithAmount(456.10m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 11, 20, 12, 0, 0, TimeSpan.Zero));

        using var response = await client.PostCancellationWithNoBody(reference);

        var problem = await ProblemAssert.SingleRuleViolation(response, "CANCEL-7", Title, PolicyActions.CancellationPath(reference));
        Assert.Equal(new DateOnly(2027, 11, 7), problem.GetDate("latestEndDate"));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 7), "Expired");
    }
}
