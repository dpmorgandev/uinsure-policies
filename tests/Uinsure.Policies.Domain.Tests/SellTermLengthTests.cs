using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-3: the client sends only the start date. A sale creates one term whose end date is calculated
// as start + 1 year - 1 day, and whose amount is the policy amount.
public sealed class SellTermLengthTests
{
    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Sale_creates_one_term_ending_one_year_less_a_day_after_its_start()
    {
        var policy = new PolicyBuilder()
            .WithStartDate(TestClock.Today.AddDays(15))
            .WithAmount(412.34m)
            .Build();

        var term = Assert.Single(policy.Terms);
        Assert.Equal(new DateOnly(2026, 11, 1), term.Start);
        Assert.Equal(new DateOnly(2027, 10, 31), term.End);
        Assert.Equal(412.34m, term.Amount);
    }

    // Today 2028-01-15 puts 2028-02-29 at today + 45, inside the sale window.
    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Policy_sold_to_start_on_29_February_2028_ends_on_28_February_2029()
    {
        var policy = new PolicyBuilder()
            .WithToday(new DateOnly(2028, 1, 15))
            .WithStartDate(new DateOnly(2028, 2, 29))
            .Build();

        var term = Assert.Single(policy.Terms);
        Assert.Equal(new DateOnly(2028, 2, 29), term.Start);
        Assert.Equal(new DateOnly(2029, 2, 28), term.End);
    }

    // Today 2027-01-15 puts 2027-03-01 at today + 45; the term spans 29 Feb 2028.
    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Policy_sold_to_start_on_1_March_2027_ends_on_29_February_2028()
    {
        var policy = new PolicyBuilder()
            .WithToday(new DateOnly(2027, 1, 15))
            .WithStartDate(new DateOnly(2027, 3, 1))
            .Build();

        var term = Assert.Single(policy.Terms);
        Assert.Equal(new DateOnly(2027, 3, 1), term.Start);
        Assert.Equal(new DateOnly(2028, 2, 29), term.End);
    }
}
