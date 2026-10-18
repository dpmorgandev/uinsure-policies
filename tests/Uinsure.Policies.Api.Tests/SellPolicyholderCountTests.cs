using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-4: a policy has 1 to 3 policyholders, otherwise 422.
public sealed class SellPolicyholderCountTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Wrong number of policyholders";

    private readonly PoliciesApiFactory _factory;

    public SellPolicyholderCountTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public async Task One_policyholder_is_accepted_and_reads_back()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL4-ONE")
            .WithPolicyholders(new PolicyholderBuilder().WithName("Ada", "Lovelace").WithDateOfBirth(new DateOnly(1985, 12, 10)))
            .Build();

        var policy = await client.SellAndReadBack(body, "SELL4-ONE");

        var holder = Assert.Single(policy.GetProperty("policyholders").EnumerateArray());
        Assert.Equal("Ada", holder.GetProperty("firstName").GetString());
        Assert.Equal("Lovelace", holder.GetProperty("lastName").GetString());
        Assert.Equal(new DateOnly(1985, 12, 10), holder.GetDate("dateOfBirth"));
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public async Task Three_policyholders_are_accepted_and_read_back_in_order()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL4-THREE")
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Ada", "Lovelace"),
                new PolicyholderBuilder().WithName("Charles", "Babbage"),
                new PolicyholderBuilder().WithName("Mary", "Somerville"))
            .Build();

        var policy = await client.SellAndReadBack(body, "SELL4-THREE");

        var lastNames = policy.GetProperty("policyholders").EnumerateArray()
            .Select(holder => holder.GetProperty("lastName").GetString());
        Assert.Equal(["Lovelace", "Babbage", "Somerville"], lastNames);
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public async Task Zero_policyholders_are_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL4-ZERO").WithPolicyholders().Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-4", Title);
        Assert.Equal(0, problem.GetProperty("count").GetInt32());
        await client.AssertNotStored("SELL4-ZERO");
    }

    [Fact]
    [Trait("Rule", "SELL-4")]
    public async Task Four_policyholders_are_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL4-FOUR")
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Ada", "Lovelace"),
                new PolicyholderBuilder().WithName("Charles", "Babbage"),
                new PolicyholderBuilder().WithName("Mary", "Somerville"),
                new PolicyholderBuilder().WithName("Alan", "Turing"))
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-4", Title);
        Assert.Equal(4, problem.GetProperty("count").GetInt32());
        await client.AssertNotStored("SELL4-FOUR");
    }
}
