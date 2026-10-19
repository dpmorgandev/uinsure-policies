using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-1/2: a renewal is allowed from the latest term's end - 30 days to its end date, inclusive; otherwise a
// 422 RENEW-1/2 violation with the extensions windowStart and windowEnd, and the policy keeps its one term.
// RENEW-1 is the opening edge (end - 31 rejected, end - 30 accepted), RENEW-2 the closing edge (the end date
// accepted, end + 1 rejected). Each side of each edge is its own test. The builder's default policy is sold on
// TestClock.Today (2026-10-17), auto-renewing by DirectDebit:
//   start 2026-11-08 ends 2027-11-07 (365 days); window 2027-10-08 to 2027-11-07;
//   the renewed term runs 2027-11-08 to 2028-11-07 (366 days, through 29 Feb 2028);
//   renewed once, the window moves to 2028-10-08 to 2028-11-07 (the latest term's end).
public sealed class RenewWindowTests
{
    private const string RuleId = "RENEW-1/2";

    private static readonly DateOnly WindowStart = new(2027, 10, 8);
    private static readonly DateOnly End = new(2027, 11, 7);
    private static readonly DateOnly RenewedStart = new(2027, 11, 8);
    private static readonly DateOnly RenewedEnd = new(2028, 11, 7);

    [Fact]
    [Trait("Rule", "RENEW-1")]
    public void Renewal_31_days_before_the_end_is_rejected_giving_the_window()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(548.21m, null, new DateOnly(2027, 10, 7), new PaymentReferences().Next);

        AssertOutsideTheWindow(result, WindowStart, End);
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "RENEW-1")]
    public void Renewal_30_days_before_the_end_is_accepted()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(548.21m, null, new DateOnly(2027, 10, 8), new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, RenewedStart, RenewedEnd, 548.21m, new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 548.21m));
        Assert.Equal(2, policy.Terms.Count);
    }

    // Sold today, the policy's window opens almost a year away.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public void Renewal_on_the_day_of_sale_is_rejected_giving_the_window()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(548.21m, null, TestClock.Today, new PaymentReferences().Next);

        AssertOutsideTheWindow(result, WindowStart, End);
        Assert.Single(policy.Terms);
    }

    [Fact]
    [Trait("Rule", "RENEW-2")]
    public void Renewal_on_the_end_date_is_accepted()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(551.94m, null, End, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        TermAssert.Is(result.Value, RenewedStart, RenewedEnd, 551.94m, new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 551.94m));
        Assert.Equal(2, policy.Terms.Count);
    }

    [Fact]
    [Trait("Rule", "RENEW-2")]
    public void Renewal_the_day_after_the_end_is_rejected_giving_the_window()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(551.94m, null, new DateOnly(2027, 11, 8), new PaymentReferences().Next);

        AssertOutsideTheWindow(result, WindowStart, End);
        Assert.Single(policy.Terms);
    }

    // Renewed on 2027-10-20, the latest term ends 2028-11-07: a second renewal the same day is 31 days and a
    // year early, although the first term's window is still open.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public void Second_renewal_inside_the_first_terms_window_is_rejected_as_the_window_is_the_latest_terms()
    {
        var policy = new PolicyBuilder().Build();
        var references = new PaymentReferences();
        Assert.True(policy.Renew(548.21m, null, new DateOnly(2027, 10, 20), references.Next).IsSuccess);

        var result = policy.Renew(566.05m, null, new DateOnly(2027, 10, 20), references.Next);

        AssertOutsideTheWindow(result, new DateOnly(2028, 10, 8), RenewedEnd);
        Assert.Equal(2, policy.Terms.Count);
    }

    // The latest term's end - 31, once renewed: 2028-10-07.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public void Second_renewal_31_days_before_the_latest_end_is_rejected()
    {
        var policy = new PolicyBuilder().Build();
        var references = new PaymentReferences();
        Assert.True(policy.Renew(548.21m, null, new DateOnly(2027, 10, 20), references.Next).IsSuccess);

        var result = policy.Renew(566.05m, null, new DateOnly(2028, 10, 7), references.Next);

        AssertOutsideTheWindow(result, new DateOnly(2028, 10, 8), RenewedEnd);
        Assert.Equal(2, policy.Terms.Count);
    }

    // The latest term's end + 1, once renewed: 2028-11-08. The first term's end has long passed.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public void Second_renewal_the_day_after_the_latest_end_is_rejected()
    {
        var policy = new PolicyBuilder().Build();
        var references = new PaymentReferences();
        Assert.True(policy.Renew(548.21m, null, new DateOnly(2027, 10, 20), references.Next).IsSuccess);

        var result = policy.Renew(566.05m, null, new DateOnly(2028, 11, 8), references.Next);

        AssertOutsideTheWindow(result, new DateOnly(2028, 10, 8), RenewedEnd);
        Assert.Equal(2, policy.Terms.Count);
    }

    private static void AssertOutsideTheWindow(Result<Term> result, DateOnly windowStart, DateOnly windowEnd)
    {
        var violation = ViolationAssert.SingleBusinessRule(result, RuleId);
        Assert.Equal(windowStart, ViolationAssert.Extension(violation, "windowStart"));
        Assert.Equal(windowEnd, ViolationAssert.Extension(violation, "windowEnd"));
    }
}
