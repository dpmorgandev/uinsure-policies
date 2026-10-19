using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-3: the cancellation date is on ?? today, and never before today (422, with the extension today). A
// rejected cancellation leaves the policy uncancelled. The date after cover is CancelAfterCoverTests'.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, ending 2027-11-07, 527.18.
public sealed class CancelDateTests
{
    private static readonly DateOnly Today = TestClock.Today;

    // A later UK today than the sale day, so the date filled in can only have come from today:
    // 2026-11-30 is start + 22, 23 days used, 342 unused; 527.18 × 342 ÷ 365 = 493.9604… → 493.96.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public void Cancel_with_no_date_is_dated_today()
    {
        var policy = new PolicyBuilder().Build();
        var today = new DateOnly(2026, 11, 30);

        var result = policy.Cancel(null, today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(today, result.Value.CancellationDate);
        Assert.Equal([new RefundCalculation(493.96m, "CANCEL-1c", 365, 23, 342)], result.Value.Terms);
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public void Cancel_dated_today_is_accepted()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(Today, Today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(Today, result.Value.CancellationDate);
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public void Cancel_dated_yesterday_is_rejected_as_past()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(Today.AddDays(-1), Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "CANCEL-3");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "today"));
        Assert.Null(policy.Cancellation);
    }
}
