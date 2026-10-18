using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The amount's 400 shape rows (ruling H6): an amount with more than 2 decimal places, or above 1,000,000.00,
// is rejected before any rule runs, with errors["amount"], and never rounded, so nothing ambiguous is stored.
// A zero or negative amount within that scale stays the 422 SELL-7. Each side of each limit is its own test.
// Shape is not a numbered rule, so the shape rows carry no Rule trait. The negative rows (finding A1/I2, I3)
// show the shape check hands any negative amount with at most 2 decimal places, however large, over to
// SELL-7, so they carry its trait.
public sealed class SellAmountShapeTests : IClassFixture<PoliciesApiFactory>
{
    private const string Sell7Title = "Policy amount must be greater than zero";

    private readonly PoliciesApiFactory _factory;

    public SellAmountShapeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Amount_with_2_decimal_places_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("AMOUNT-2DP").WithAmount(803.47m).Build();

        var policy = await client.SellAndReadBack(body, "AMOUNT-2DP");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "AMOUNT-2DP", new DateOnly(2027, 11, 7));
        Assert.Equal(803.47m, policy.Member("amount").GetDecimal());
    }

    [Fact]
    public async Task Amount_with_3_decimal_places_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("AMOUNT-3DP").WithAmount(527.185m).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "amount");
        await client.AssertNotStored("AMOUNT-3DP");
    }

    // Below a penny is a scale error, so it is the 400, not the 422 SELL-7 of a zero amount.
    [Fact]
    public async Task Amount_of_0_001_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("AMOUNT-0-001").WithAmount(0.001m).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "amount");
        await client.AssertNotStored("AMOUNT-0-001");
    }

    [Fact]
    public async Task Amount_of_1_000_000_00_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("AMOUNT-1M").WithAmount(1_000_000.00m).Build();

        var policy = await client.SellAndReadBack(body, "AMOUNT-1M");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "AMOUNT-1M", new DateOnly(2027, 11, 7));
        Assert.Equal(1_000_000.00m, policy.Member("amount").GetDecimal());
    }

    [Fact]
    public async Task Amount_of_1_000_000_01_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("AMOUNT-1M-01").WithAmount(1_000_000.01m).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "amount");
        await client.AssertNotStored("AMOUNT-1M-01");
    }

    // Below decimal.MinValue / 100, amount * 100 cannot be represented; the shape check must still answer.
    [Fact]
    [Trait("Rule", "SELL-7")]
    public async Task Amount_of_minus_10_to_the_27_is_rejected_with_422_SELL_7()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL7-NEG-E27").WithAmount(-1_000_000_000_000_000_000_000_000_000m).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-7", Sell7Title);
        Assert.Equal(-1_000_000_000_000_000_000_000_000_000m, problem.Member("amount").GetDecimal());
        await client.AssertNotStored("SELL7-NEG-E27");
    }
}
