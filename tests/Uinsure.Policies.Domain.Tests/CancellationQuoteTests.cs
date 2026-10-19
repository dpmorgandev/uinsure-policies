using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-1: policy.QuoteCancellation(on, today) is a read-only version of the cancel calculation. Its Terms are what
// Cancel would give for the same date, under the same rules (CANCEL-3, CANCEL-4, CANCEL-7, COULD-2), and changes nothing.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, ending 2027-11-07, 527.18;
//   start 2026-10-17 ends 2027-10-16 (365 days).
public sealed class CancellationQuoteTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // 2026-11-23 is start + 15: 527.18 × 349 ÷ 365 = 504.0707… → 504.07, in the quote and in the cancellation.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-1c")]
    public void Quote_is_the_calculation_the_cancellation_then_applies()
    {
        var policy = new PolicyBuilder().Build();

        var quote = policy.QuoteCancellation(new DateOnly(2026, 11, 23), Today);
        var cancellation = policy.Cancel(new DateOnly(2026, 11, 23), Today, new RefundReferences().Next);

        Assert.True(quote.IsSuccess);
        Assert.Equal([new RefundCalculation(504.07m, "CANCEL-1c", 365, 16, 349)], quote.Value.Terms);
        Assert.Equal(quote.Value.Terms, cancellation.Value.Terms);
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    public void Quote_leaves_the_policy_uncancelled()
    {
        var policy = new PolicyBuilder().Build();

        Assert.True(policy.QuoteCancellation(Today, Today).IsSuccess);

        Assert.Null(policy.Cancellation);
        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(Today));
        Assert.True(policy.Cancel(Today, Today, new RefundReferences().Next).IsSuccess);
    }

    // UK today 2026-11-05 on a policy started 2026-10-17: 20 days used, 345 unused;
    // 410.00 × 345 ÷ 365 = 387.5342… → 387.53.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public void Quote_with_no_date_is_for_today()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).WithAmount(410.00m).Build();

        var quote = policy.QuoteCancellation(null, new DateOnly(2026, 11, 5));

        Assert.Equal([new RefundCalculation(387.53m, "CANCEL-1c", 365, 20, 345)], quote.Value.Terms);
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public void Quote_dated_yesterday_is_rejected_as_past()
    {
        var policy = new PolicyBuilder().Build();

        var quote = policy.QuoteCancellation(Today.AddDays(-1), Today);

        var violation = ViolationAssert.SingleBusinessRule(quote, "CANCEL-3");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "today"));
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-7")]
    public void Quote_dated_the_day_after_the_end_is_rejected_as_after_cover()
    {
        var policy = new PolicyBuilder().Build();

        var quote = policy.QuoteCancellation(new DateOnly(2027, 11, 8), Today);

        var violation = ViolationAssert.SingleBusinessRule(quote, "CANCEL-7");
        Assert.Equal(new DateOnly(2027, 11, 7), ViolationAssert.Extension(violation, "latestEndDate"));
    }

    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-4")]
    public void Quote_on_a_cancelled_policy_is_a_conflict_giving_the_cancellation_date()
    {
        var policy = new PolicyBuilder().Build();
        Assert.True(policy.Cancel(new DateOnly(2026, 12, 1), Today, new RefundReferences().Next).IsSuccess);

        var quote = policy.QuoteCancellation(null, Today);

        var violation = ViolationAssert.SingleConflict(quote, "CANCEL-4");
        Assert.Equal(new DateOnly(2026, 12, 1), ViolationAssert.Extension(violation, "cancellationDate"));
    }

    // Claimed on the start date, quoted on start + 14: cooling-off would refund in full; the claim makes it 0.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "COULD-2")]
    public void Quote_after_a_claim_refunds_nothing()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).WithAmount(275.90m).Build();
        Assert.True(policy.RecordClaim(Today, "Storm damage to the garage roof", Today).IsSuccess);

        var quote = policy.QuoteCancellation(new DateOnly(2026, 10, 31), Today);

        Assert.Equal([new RefundCalculation(0m, "COULD-2", 365, 15, 350)], quote.Value.Terms);
    }
}
