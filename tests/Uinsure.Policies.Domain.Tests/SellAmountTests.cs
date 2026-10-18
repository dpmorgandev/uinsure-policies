using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-7 (added): the policy amount must be greater than 0, else 422. The amount extension carries it.
public sealed class SellAmountTests
{
    [Fact]
    [Trait("Rule", "SELL-7")]
    public void Amount_of_one_penny_is_accepted()
    {
        var policy = new PolicyBuilder().WithAmount(0.01m).Build();

        Assert.Equal(0.01m, Assert.Single(policy.Terms).Amount);
    }

    [Fact]
    [Trait("Rule", "SELL-7")]
    public void Amount_of_zero_is_rejected()
    {
        var result = new PolicyBuilder().WithAmount(0.00m).Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-7");
        Assert.Equal(0.00m, ViolationAssert.Extension(violation, "amount"));
    }

    [Fact]
    [Trait("Rule", "SELL-7")]
    public void Negative_amount_is_rejected()
    {
        var result = new PolicyBuilder().WithAmount(-0.01m).Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-7");
        Assert.Equal(-0.01m, ViolationAssert.Extension(violation, "amount"));
    }
}
