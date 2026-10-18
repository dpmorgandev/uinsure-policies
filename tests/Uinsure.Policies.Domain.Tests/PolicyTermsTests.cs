using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// The current-term view (requirements GET-1): the current term is the term in force today, or the latest
// term if today is outside them all. A one-term policy sold to start 2026-11-01 (TestClock.Today + 15) is
// covered from 2026-11-01 to 2027-10-31 inclusive. Untraited until F2 adds the GET-1 traits.
public sealed class PolicyTermsTests
{
    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    [Fact]
    public void Latest_term_of_a_one_term_policy_is_its_only_term()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var latest = policy.LatestTerm;

        Assert.Same(Assert.Single(policy.Terms), latest);
        Assert.NotNull(latest);
        Assert.Equal(new DateOnly(2026, 11, 1), latest.Start);
        Assert.Equal(new DateOnly(2027, 10, 31), latest.End);
    }

    [Fact]
    public void Current_term_on_the_day_before_the_start_is_the_latest_term()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2026, 10, 31));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    public void Current_term_on_the_start_day_is_the_term_covering_it()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2026, 11, 1));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    public void Current_term_on_the_end_day_is_the_term_covering_it()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2027, 10, 31));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    public void Current_term_on_the_day_after_the_end_is_the_latest_term()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2027, 11, 1));

        Assert.Same(Assert.Single(policy.Terms), current);
    }
}
