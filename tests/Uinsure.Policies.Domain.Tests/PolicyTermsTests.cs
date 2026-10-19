using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// The current-term view (requirements GET-1): the current term is the term in force today, or the latest
// term if today is outside them all. A one-term policy sold to start 2026-11-01 (TestClock.Today + 15) is
// covered from 2026-11-01 to 2027-10-31 inclusive. The two-term rows (finding A1/I5) restore that term
// followed by 2027-11-01 to 2028-10-31, so the first and latest terms differ and each member is pinned to the
// right one. Traited GET-1 in F2.
public sealed class PolicyTermsTests
{
    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    private static Policy TwoTermPolicy() => new PolicyBuilder().RestoreWithTerms(
        new Term(new DateOnly(2026, 11, 1), 527.18m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m)),
        new Term(new DateOnly(2027, 11, 1), 561.09m, new Payment("PAY-DOM-0042-R1", PaymentMethod.DirectDebit, 561.09m)));

    [Fact]
    [Trait("Rule", "GET-1")]
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
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_day_before_the_start_is_the_latest_term()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2026, 10, 31));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_start_day_is_the_term_covering_it()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2026, 11, 1));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_end_day_is_the_term_covering_it()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2027, 10, 31));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_day_after_the_end_is_the_latest_term()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        var current = policy.CurrentTerm(new DateOnly(2027, 11, 1));

        Assert.Same(Assert.Single(policy.Terms), current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Latest_term_of_a_two_term_policy_is_the_second_term()
    {
        var policy = TwoTermPolicy();

        var latest = policy.LatestTerm;

        Assert.Same(policy.Terms[1], latest);
        Assert.Equal(new DateOnly(2027, 11, 1), latest.Start);
        Assert.Equal(new DateOnly(2028, 10, 31), latest.End);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_a_day_inside_the_first_of_two_terms_is_the_first_term()
    {
        var policy = TwoTermPolicy();

        var current = policy.CurrentTerm(new DateOnly(2027, 6, 1));

        Assert.Same(policy.Terms[0], current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_end_day_of_the_first_of_two_terms_is_the_first_term()
    {
        var policy = TwoTermPolicy();

        var current = policy.CurrentTerm(new DateOnly(2027, 10, 31));

        Assert.Same(policy.Terms[0], current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_on_the_start_day_of_the_second_of_two_terms_is_the_second_term()
    {
        var policy = TwoTermPolicy();

        var current = policy.CurrentTerm(new DateOnly(2027, 11, 1));

        Assert.Same(policy.Terms[1], current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_before_the_first_start_of_two_terms_is_the_latest_term()
    {
        var policy = TwoTermPolicy();

        var current = policy.CurrentTerm(new DateOnly(2026, 10, 31));

        Assert.Same(policy.Terms[1], current);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Current_term_after_the_end_of_two_terms_is_the_latest_term()
    {
        var policy = TwoTermPolicy();

        var current = policy.CurrentTerm(new DateOnly(2028, 11, 1));

        Assert.Same(policy.Terms[1], current);
    }
}
