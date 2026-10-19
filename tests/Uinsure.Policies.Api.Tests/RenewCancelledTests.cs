using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-6: a cancelled policy cannot be renewed: 409 /problems/renew-6, with the cancellation's date as the
// cancellationDate extension and in the detail, and no violations[]. It is a state conflict, so it is returned
// alone, before every 422 rule the request also breaks. The policy reads back exactly as cancelled, with its one
// refund, and no new term. Each policy is sold (E1) and cancelled (E3) with the clock at TestClock.Now:
//   start 2026-10-20 ends 2027-10-19, cancelled with effect from 2027-10-05 (CANCEL-1c: 365 days, 351 used,
//   14 unused; 388.21 x 14 / 365 = 14.8902... to 14.89 by Card), then renewed inside its window on 2027-09-25;
//   start 2026-10-17 ends 2027-10-16, cancelled today (CANCEL-1b: 352.48 in full by Cheque), then renewed today.
public sealed class RenewCancelledTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Cancelled policy cannot be renewed";

    private readonly PoliciesApiFactory _factory;

    public RenewCancelledTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Inside the window, with a valid amount: only the cancellation stands in the way.
    [Fact]
    [Trait("Rule", "RENEW-6")]
    public async Task Renewal_of_a_cancelled_policy_is_rejected_with_409()
    {
        const string reference = "RENEW6-CANCELLED";
        var cancelOn = new DateOnly(2027, 10, 5);
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 10, 20)).WithAmount(388.21m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        var refundReference = Assert.Single(CancellationAssert.IsCancellation(
            await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancelOn).Build()),
            new ExpectedCancellation(reference, cancelOn, "CANCEL-1c", "14.89",
                [new ExpectedTerm(1, "CANCEL-1c", 365, 351, 14, "14.89")],
                [new ExpectedRefund("Card", "14.89", paymentReference)])));
        _factory.MoveClockTo(new DateOnly(2027, 9, 25));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(401.75m).Build());

        var problem = await ProblemAssert.Conflict(response, "RENEW-6", Title, RenewalActions.RenewalsPath(reference));
        Assert.Equal(cancelOn, problem.GetDate("cancellationDate"));
        Assert.Contains("2027-10-05", problem.Member("detail").GetString(), StringComparison.Ordinal);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 19), "Cancelled", false,
            new ExpectedCancellationSummary(cancelOn, "CANCEL-1c", "14.89"),
            [new ExpectedPolicyRefund(refundReference, "Card", "14.89", paymentReference)],
            []);
    }

    // Renewed the day it was sold and cancelled, for 0.00 by Cheque: RENEW-1/2, RENEW-5 and COULD-3 would each
    // be a 422, but the 409 is returned alone.
    [Fact]
    [Trait("Rule", "RENEW-6")]
    public async Task Renewal_of_a_cancelled_policy_is_409_alone_before_the_422_rules()
    {
        const string reference = "RENEW6-BEFORE-422S";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(352.48m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        using var cancelled = await client.PostCancellationWithNoBody(reference);
        var refundReference = Assert.Single(CancellationAssert.IsCancellation(
            await PolicyActions.CancelledBody(cancelled),
            new ExpectedCancellation(reference, TestClock.Today, "CANCEL-1b", "352.48",
                [new ExpectedTerm(1, "CANCEL-1b", 365, 1, 364, "352.48")],
                [new ExpectedRefund("Cheque", "352.48", paymentReference)])));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(0.00m).WithPaymentMethod("Cheque").Build());

        var problem = await ProblemAssert.Conflict(response, "RENEW-6", Title, RenewalActions.RenewalsPath(reference));
        Assert.Equal(TestClock.Today, problem.GetDate("cancellationDate"));
        Assert.Contains("2026-10-17", problem.Member("detail").GetString(), StringComparison.Ordinal);
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Cancelled", false,
            new ExpectedCancellationSummary(TestClock.Today, "CANCEL-1b", "352.48"),
            [new ExpectedPolicyRefund(refundReference, "Cheque", "352.48", paymentReference)],
            []);
    }
}
