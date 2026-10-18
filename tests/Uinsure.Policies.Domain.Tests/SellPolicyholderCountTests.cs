using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-4: a policy has 1 to 3 policyholders, otherwise 422. The count extension carries how many were sent.
public sealed class SellPolicyholderCountTests
{
    [Fact]
    [Trait("Rule", "SELL-4")]
    public void No_policyholders_is_rejected()
    {
        var result = new PolicyBuilder().WithPolicyholders().Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-4");
        Assert.Equal(0, ViolationAssert.Extension(violation, "count"));
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public void One_policyholder_is_accepted()
    {
        var policy = new PolicyBuilder()
            .WithPolicyholders(new PolicyholderBuilder().WithName("Grace", "Hopper").WithDateOfBirth(new DateOnly(1990, 4, 12)))
            .Build();

        var holder = Assert.Single(policy.Policyholders);
        Assert.Equal(new Policyholder("Grace", "Hopper", new DateOnly(1990, 4, 12)), holder);
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public void Three_policyholders_are_accepted()
    {
        var policy = new PolicyBuilder()
            .WithPolicyholders(
                new PolicyholderBuilder(),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(new DateOnly(1991, 12, 26)),
                new PolicyholderBuilder().WithName("Mary", "Somerville").WithDateOfBirth(new DateOnly(1980, 12, 26)))
            .Build();

        Assert.Equal(["Johnson", "Babbage", "Somerville"], policy.Policyholders.Select(holder => holder.LastName));
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public void Four_policyholders_are_rejected()
    {
        var result = new PolicyBuilder()
            .WithPolicyholders(
                new PolicyholderBuilder(),
                new PolicyholderBuilder().WithName("Charles", "Babbage"),
                new PolicyholderBuilder().WithName("Mary", "Somerville"),
                new PolicyholderBuilder().WithName("Alan", "Turing"))
            .Sell();

        var violation = ViolationAssert.SingleBusinessRule(result, "SELL-4");
        Assert.Equal(4, ViolationAssert.Extension(violation, "count"));
    }
}
