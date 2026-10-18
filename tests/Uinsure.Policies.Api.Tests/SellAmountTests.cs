using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-7 (added): the policy amount must be greater than 0, else 422 (matching RENEW-5).
public sealed class SellAmountTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Policy amount must be greater than zero";

    private readonly PoliciesApiFactory _factory;

    public SellAmountTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-7")]
    public async Task Amount_of_one_penny_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL7-ONE-PENNY").WithAmount(0.01m).Build();

        var policy = await client.SellAndReadBack(body, "SELL7-ONE-PENNY");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL7-ONE-PENNY", new DateOnly(2027, 11, 7));
        Assert.Equal(0.01m, policy.GetProperty("amount").GetDecimal());
    }

    [Fact]
    [Trait("Rule", "SELL-7")]
    public async Task Amount_of_zero_is_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL7-ZERO").WithAmount(0.00m).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-7", Title);
        Assert.Equal(0.00m, problem.GetProperty("amount").GetDecimal());
        await client.AssertNotStored("SELL7-ZERO");
    }

    [Fact]
    [Trait("Rule", "SELL-7")]
    public async Task Negative_amount_is_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL7-NEGATIVE").WithAmount(-1.00m).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-7", Title);
        Assert.Equal(-1.00m, problem.GetProperty("amount").GetDecimal());
        await client.AssertNotStored("SELL7-NEGATIVE");
    }
}
