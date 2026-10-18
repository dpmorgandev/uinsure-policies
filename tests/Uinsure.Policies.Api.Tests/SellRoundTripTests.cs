using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Plumbing for E1 → E2: what the sale stores is what the bare GET returns. No numbered rule, so no trait.
// The full response shape, status and current-term view are F2's (GET-1).
public sealed class SellRoundTripTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellRoundTripTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Sold_policy_reads_back_with_the_values_it_was_sold_with()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("ROUND-TRIP")
            .WithType("Household")
            .WithStartDate(new DateOnly(2026, 11, 1))
            .WithAmount(365.00m)
            .WithAutoRenew(true)
            .WithAddress("1 High St", "Flat 2", "Ancoats")
            .WithPostcode("M1 1AA")
            .Build();

        var policy = await client.SellAndReadBack(body, "ROUND-TRIP");

        Assert.Equal("Household", policy.GetProperty("type").GetString());
        Assert.Equal(new DateOnly(2026, 11, 1), policy.GetDate("startDate"));
        Assert.Equal(365.00m, policy.GetProperty("amount").GetDecimal());
        Assert.True(policy.GetProperty("autoRenew").GetBoolean());
        var property = policy.GetProperty("property");
        Assert.Equal("1 High St", property.GetProperty("addressLine1").GetString());
        Assert.Equal("Flat 2", property.GetProperty("addressLine2").GetString());
        Assert.Equal("Ancoats", property.GetProperty("addressLine3").GetString());
        Assert.Equal("M1 1AA", property.GetProperty("postcode").GetString());
    }

    [Fact]
    public async Task Buy_to_let_policy_without_auto_renew_reads_back_as_sold()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("ROUND-TRIP-BTL").WithType("BuyToLet").WithAutoRenew(false).Build();

        var policy = await client.SellAndReadBack(body, "ROUND-TRIP-BTL");

        Assert.Equal("BuyToLet", policy.GetProperty("type").GetString());
        Assert.False(policy.GetProperty("autoRenew").GetBoolean());
    }
}
