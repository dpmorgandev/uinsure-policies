using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Plumbing for E1 → E2: what the sale stores is what the bare GET returns. No numbered rule, so no trait.
// Status, the current-term view and the rest of the response shape are F2's (GET-1).
public sealed class SellRoundTripTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellRoundTripTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // Every field sold differs from the builder defaults and the api.md example (Household and autoRenew
    // true stay, as the buy-to-let test below sells the other side of both), and every F1 field is read back.
    [Fact]
    public async Task Sold_policy_reads_back_with_the_values_it_was_sold_with()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("ROUND-TRIP")
            .WithType("Household")
            .WithStartDate(new DateOnly(2026, 11, 20))
            .WithAmount(298.76m)
            .WithAutoRenew(true)
            .WithPaymentMethod("Cheque")
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Rosalind", "Franklin").WithDateOfBirth(new DateOnly(1988, 7, 25)),
                new PolicyholderBuilder().WithName("Dorothy", "Hodgkin").WithDateOfBirth(new DateOnly(1979, 5, 12)))
            .WithAddress("22 Mill Lane", "Flat 2", "Ancoats")
            .WithPostcode("M4 5JD")
            .Build();

        var policy = await client.SellAndReadBack(body, "ROUND-TRIP");

        Assert.Equal("Household", policy.GetProperty("type").GetString());
        Assert.Equal(new DateOnly(2026, 11, 20), policy.GetDate("startDate"));
        Assert.Equal(298.76m, policy.GetProperty("amount").GetDecimal());
        Assert.True(policy.GetProperty("autoRenew").GetBoolean());
        var property = policy.GetProperty("property");
        Assert.Equal("22 Mill Lane", property.GetProperty("addressLine1").GetString());
        Assert.Equal("Flat 2", property.GetProperty("addressLine2").GetString());
        Assert.Equal("Ancoats", property.GetProperty("addressLine3").GetString());
        Assert.Equal("M4 5JD", property.GetProperty("postcode").GetString());
        SoldPolicyAssert.ReadsBackAsSold(policy, body, "ROUND-TRIP", new DateOnly(2027, 11, 19));
    }

    [Fact]
    public async Task Buy_to_let_policy_without_auto_renew_reads_back_as_sold()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("ROUND-TRIP-BTL").WithType("BuyToLet").WithAutoRenew(false).Build();

        var policy = await client.SellAndReadBack(body, "ROUND-TRIP-BTL");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "ROUND-TRIP-BTL", new DateOnly(2027, 11, 7));
        Assert.Equal("BuyToLet", policy.GetProperty("type").GetString());
        Assert.False(policy.GetProperty("autoRenew").GetBoolean());
    }
}
