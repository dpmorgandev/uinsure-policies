using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// Every 422 rule is evaluated and none returns early: the result lists every broken rule in rule-ID order.
public sealed class SellMultipleViolationsTests
{
    [Fact]
    [Trait("Rule", "SELL-2")]
    [Trait("Rule", "SELL-4")]
    [Trait("Rule", "SELL-5")]
    [Trait("Rule", "SELL-7")]
    public void Sale_breaking_SELL_2_SELL_4_SELL_5_and_SELL_7_reports_all_four_in_rule_order()
    {
        var start = TestClock.Today.AddDays(61);
        var result = new PolicyBuilder()
            .WithStartDate(start)
            .WithAmount(0.00m)
            .WithPolicyholders(
                new PolicyholderBuilder(),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(start.AddYears(-16).AddDays(1)),
                new PolicyholderBuilder().WithName("Mary", "Somerville"),
                new PolicyholderBuilder().WithName("Alan", "Turing"))
            .Sell();

        Assert.False(result.IsSuccess);
        Assert.Equal(["SELL-2", "SELL-4", "SELL-5", "SELL-7"], result.Violations.Select(violation => violation.RuleId));
        Assert.All(result.Violations, violation => Assert.Equal(ViolationKind.BusinessRule, violation.Kind));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    [Trait("Rule", "SELL-7")]
    public void Backdated_sale_with_a_zero_amount_reports_SELL_2_then_SELL_7()
    {
        var result = new PolicyBuilder()
            .WithStartDate(TestClock.Today.AddDays(-1))
            .WithAmount(0.00m)
            .Sell();

        Assert.False(result.IsSuccess);
        Assert.Equal(["SELL-2", "SELL-7"], result.Violations.Select(violation => violation.RuleId));
    }
}
