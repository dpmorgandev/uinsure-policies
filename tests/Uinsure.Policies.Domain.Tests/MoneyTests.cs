using Uinsure.Policies.Domain.Common;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-6: money is rounded to 2 dp, half away from zero, in one place (Money.Round). .NET's default is
// banker's rounding (half to even), which would take 50.005 to 50.00; rounding toward positive infinity would
// take -50.005 to -50.00; a ceiling would take 50.004999 to 50.01. Each of those is caught by one test here.
public sealed class MoneyTests
{
    [Fact]
    [Trait("Rule", "CANCEL-6")]
    public void Half_a_penny_on_an_even_penny_rounds_away_from_zero()
    {
        Assert.Equal(50.01m, Money.Round(50.005m));
    }

    [Fact]
    [Trait("Rule", "CANCEL-6")]
    public void Negative_half_a_penny_rounds_away_from_zero()
    {
        Assert.Equal(-50.01m, Money.Round(-50.005m));
    }

    [Fact]
    [Trait("Rule", "CANCEL-6")]
    public void Just_under_half_a_penny_rounds_down()
    {
        Assert.Equal(50.00m, Money.Round(50.004999m));
    }

    // 527.18 × 349 ÷ 365, the start + 15 refund of the builder's default policy.
    [Fact]
    [Trait("Rule", "CANCEL-6")]
    public void Quotient_with_more_than_two_places_rounds_to_the_nearest_penny()
    {
        Assert.Equal(504.07m, Money.Round(504.0707397260273972602739726m));
    }
}
