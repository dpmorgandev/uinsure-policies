using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-7 (added): the cancellation date must be on or before the latest term's end, else 422 with the extension
// latestEndDate. With CANCEL-3 this blocks cancelling an expired policy. CANCEL-3 and CANCEL-7 are both collected;
// neither returns early. A rejected cancellation leaves the policy uncancelled.
//   The builder's default sells on 2026-10-17 to start 2026-11-08, ending 2027-11-07;
//   RestoredPolicy.CardThenDirectDebit is 2026-03-01 to 2027-02-28 then 2027-03-01 to 2028-02-29.
public sealed class CancelAfterCoverTests
{
    private static readonly DateOnly Today = TestClock.Today;

    [Fact]
    [Trait("Rule", "CANCEL-7")]
    public void Cancel_on_the_last_day_of_cover_is_accepted()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(new DateOnly(2027, 11, 7), Today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2027, 11, 7), result.Value.CancellationDate);
    }

    [Fact]
    [Trait("Rule", "CANCEL-7")]
    public void Cancel_on_the_day_after_the_end_is_rejected_as_after_cover()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Cancel(new DateOnly(2027, 11, 8), Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "CANCEL-7");
        Assert.Equal(new DateOnly(2027, 11, 7), ViolationAssert.Extension(violation, "latestEndDate"));
        Assert.Null(policy.Cancellation);
    }

    // The window runs to the latest term's end, so the day after the first term's end is still in cover.
    [Fact]
    [Trait("Rule", "CANCEL-7")]
    public void Cancel_after_the_first_terms_end_but_within_the_latest_is_accepted()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();

        var result = policy.Cancel(new DateOnly(2027, 3, 1), Today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2027, 3, 1), result.Value.CancellationDate);
    }

    [Fact]
    [Trait("Rule", "CANCEL-7")]
    public void Cancel_on_the_day_after_the_latest_terms_end_is_rejected()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();

        var result = policy.Cancel(new DateOnly(2028, 3, 1), Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "CANCEL-7");
        Assert.Equal(new DateOnly(2028, 2, 29), ViolationAssert.Extension(violation, "latestEndDate"));
    }

    // The term 2025-10-17 to 2026-10-16 ended yesterday: today, the default, is not past but is after cover.
    // CANCEL-7 alone.
    [Fact]
    [Trait("Rule", "CANCEL-7")]
    [Trait("Rule", "CANCEL-3")]
    public void Cancelling_an_expired_policy_today_is_rejected_as_after_cover()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 17));

        var result = policy.Cancel(null, Today, new RefundReferences().Next);

        var violation = ViolationAssert.SingleBusinessRule(result, "CANCEL-7");
        Assert.Equal(new DateOnly(2026, 10, 16), ViolationAssert.Extension(violation, "latestEndDate"));
        Assert.Null(policy.Cancellation);
    }

    // The term 2025-10-11 to 2026-10-10: 2026-10-12 is both before today and after the end. Both are reported,
    // CANCEL-3 first, each with its own extension.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    [Trait("Rule", "CANCEL-7")]
    public void Cancel_dated_in_the_past_and_after_cover_reports_CANCEL_3_and_CANCEL_7()
    {
        var policy = RestoredPolicy.WithTermsStarting(new DateOnly(2025, 10, 11));

        var result = policy.Cancel(new DateOnly(2026, 10, 12), Today, new RefundReferences().Next);

        ViolationAssert.BusinessRules(result, "CANCEL-3", "CANCEL-7");
        Assert.Equal(Today, ViolationAssert.Extension(result.Violations[0], "today"));
        Assert.Equal(new DateOnly(2026, 10, 10), ViolationAssert.Extension(result.Violations[1], "latestEndDate"));
        Assert.Null(policy.Cancellation);
    }
}
