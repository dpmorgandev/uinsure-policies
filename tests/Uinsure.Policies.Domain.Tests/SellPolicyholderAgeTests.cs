using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-5: every policyholder is 16 or older on the start date; a 16th birthday on the start date passes.
// The index extension is the policyholder's 1-based position. The start here is 2026-11-01 (today + 15).
public sealed class SellPolicyholderAgeTests
{
    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Policyholder_whose_16th_birthday_is_the_start_date_is_accepted()
    {
        var result = new PolicyBuilder()
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(new DateOnly(2010, 11, 1)))
            .Sell();

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2010, 11, 1), Assert.Single(result.Value.Policyholders).DateOfBirth);
    }

    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Policyholder_whose_16th_birthday_is_the_day_after_the_start_date_is_rejected()
    {
        var result = new PolicyBuilder()
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(new DateOnly(2010, 11, 2)))
            .Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-5");
        Assert.Equal(1, ViolationAssert.Extension(violation, "index"));
    }

    // Age is measured on the start date, not today: 15 on today (2026-10-17), 16 by the start (2026-11-01).
    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Policyholder_under_16_today_but_16_on_the_start_date_is_accepted()
    {
        var result = new PolicyBuilder()
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(new DateOnly(2010, 10, 25)))
            .Sell();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Second_policyholder_under_16_on_the_start_date_is_rejected_and_identified_by_index_2()
    {
        var result = new PolicyBuilder()
            .WithStartDate(Start)
            .WithPolicyholders(
                new PolicyholderBuilder(),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(new DateOnly(2011, 1, 1)))
            .Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-5");
        Assert.Equal(2, ViolationAssert.Extension(violation, "index"));
    }

    // A 29 Feb birthday needs no special case: born 2012-02-29, the 16th birthday is 2028-02-29.
    // Today 2028-01-15 puts both start dates inside the sale window.
    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Policyholder_born_on_29_February_2012_is_accepted_for_a_start_on_29_February_2028()
    {
        var result = new PolicyBuilder()
            .WithToday(new DateOnly(2028, 1, 15))
            .WithStartDate(new DateOnly(2028, 2, 29))
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(new DateOnly(2012, 2, 29)))
            .Sell();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    [Trait("Rule", "SELL-5")]
    public void Policyholder_born_on_29_February_2012_is_rejected_for_a_start_on_28_February_2028()
    {
        var result = new PolicyBuilder()
            .WithToday(new DateOnly(2028, 1, 15))
            .WithStartDate(new DateOnly(2028, 2, 28))
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(new DateOnly(2012, 2, 29)))
            .Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-5");
        Assert.Equal(1, ViolationAssert.Extension(violation, "index"));
    }
}
