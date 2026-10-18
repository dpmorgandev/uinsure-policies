using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-3: a term is always one year, cover inclusive: end = start + 1 year - 1 day. A 29 Feb start
// ends on 28 Feb of the next year.
public sealed class TermTests
{
    [Fact]
    [Trait("Rule", "SELL-3")]
    public void End_date_is_one_year_less_a_day_after_the_start()
    {
        Assert.Equal(new DateOnly(2027, 10, 31), Term.EndFor(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Start_on_1_March_2027_ends_on_29_February_2028()
    {
        Assert.Equal(new DateOnly(2028, 2, 29), Term.EndFor(new DateOnly(2027, 3, 1)));
    }

    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Start_on_28_February_2028_ends_on_27_February_2029()
    {
        Assert.Equal(new DateOnly(2029, 2, 27), Term.EndFor(new DateOnly(2028, 2, 28)));
    }

    // The named 29 February test: there is no 29 Feb 2029, and the day before 1 Mar 2029 is 28 Feb.
    [Fact]
    [Trait("Rule", "SELL-3")]
    public void Start_on_29_February_2028_ends_on_28_February_2029()
    {
        Assert.Equal(new DateOnly(2029, 2, 28), Term.EndFor(new DateOnly(2028, 2, 29)));
    }
}
