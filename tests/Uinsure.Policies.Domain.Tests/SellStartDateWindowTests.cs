using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-2: the start date must be between today and today + 60, inclusive. No backdating. Today is the
// DateOnly passed in (2026-10-17), so the window is 2026-10-17 to 2026-12-16.
public sealed class SellStartDateWindowTests
{
    [Fact]
    [Trait("Rule", "SELL-2")]
    public void Start_date_yesterday_is_rejected()
    {
        var result = new PolicyBuilder().WithStartDate(TestClock.Today.AddDays(-1)).Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-2");
        Assert.Equal(TestClock.Today, ViolationAssert.Extension(violation, "earliest"));
        Assert.Equal(TestClock.Today.AddDays(60), ViolationAssert.Extension(violation, "latest"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public void Start_date_today_is_accepted()
    {
        var result = new PolicyBuilder().WithStartDate(TestClock.Today).Sell();

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 10, 17), Assert.Single(result.Value.Terms).Start);
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public void Start_date_today_plus_60_is_accepted()
    {
        var result = new PolicyBuilder().WithStartDate(TestClock.Today.AddDays(60)).Sell();

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 12, 16), Assert.Single(result.Value.Terms).Start);
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public void Start_date_today_plus_61_is_rejected()
    {
        var result = new PolicyBuilder().WithStartDate(TestClock.Today.AddDays(61)).Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-2");
        Assert.Equal(TestClock.Today, ViolationAssert.Extension(violation, "earliest"));
        Assert.Equal(TestClock.Today.AddDays(60), ViolationAssert.Extension(violation, "latest"));
    }

    // The window moves with the today passed in: 2026-12-16 is today + 61 from 2026-10-16.
    [Fact]
    [Trait("Rule", "SELL-2")]
    public void Window_is_measured_from_the_today_passed_in()
    {
        var result = new PolicyBuilder()
            .WithToday(TestClock.Today.AddDays(-1))
            .WithStartDate(TestClock.Today.AddDays(60))
            .Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-2");
        Assert.Equal(new DateOnly(2026, 10, 16), ViolationAssert.Extension(violation, "earliest"));
        Assert.Equal(new DateOnly(2026, 12, 15), ViolationAssert.Extension(violation, "latest"));
    }
}
