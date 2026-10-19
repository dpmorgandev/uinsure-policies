using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-4: a cancelled policy can't be cancelled again: a 409 conflict, giving the original cancellation date
// (extension cancellationDate). A conflict short-circuits, so no 422 date rule is reported beside it, and the
// original cancellation is kept. The quote side is CancellationQuoteTests.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, ending 2027-11-07.
public sealed class CancelTwiceTests
{
    private static readonly DateOnly Today = TestClock.Today;

    [Fact]
    [Trait("Rule", "CANCEL-4")]
    public void Second_cancel_is_a_conflict_giving_the_original_date()
    {
        var policy = new PolicyBuilder().Build();
        var first = policy.Cancel(new DateOnly(2026, 12, 1), Today, new RefundReferences().Next).Value;

        var result = policy.Cancel(null, Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleConflict(result, "CANCEL-4");
        Assert.Equal(new DateOnly(2026, 12, 1), ViolationAssert.Extension(violation, "cancellationDate"));
        Assert.Same(first, policy.Cancellation);
    }

    // Yesterday would be CANCEL-3, but the conflict is returned alone.
    [Fact]
    [Trait("Rule", "CANCEL-4")]
    public void Second_cancel_with_a_past_date_is_only_the_conflict()
    {
        var policy = new PolicyBuilder().Build();
        Assert.True(policy.Cancel(Today, Today, new RefundReferences().Next).IsSuccess);

        var result = policy.Cancel(Today.AddDays(-1), Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleConflict(result, "CANCEL-4");
        Assert.Equal(Today, ViolationAssert.Extension(violation, "cancellationDate"));
    }
}
