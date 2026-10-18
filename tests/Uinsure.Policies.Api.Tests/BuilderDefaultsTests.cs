using System.Globalization;
using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Plumbing guard, so no Rule trait: Build()'s defaults never equal the E1 example payload in the API
// contract (names, dates, amounts, address), so a stub canned from that example can't pass a test that
// sells the defaults. The example values are copied in as literals.
public sealed class BuilderDefaultsTests
{
    private static readonly DateOnly ExampleStartDate = new(2026, 11, 1);
    private const decimal ExampleAmount = 365.00m;
    private const string ExampleAddressLine1 = "1 High St";
    private const string ExamplePostcode = "M1 1AA";

    // The default start is a valid sale (SELL-2 window) but no boundary the SELL-2 tests own (today,
    // today + 60), not the example's start (today + 15) and not Distinctive's start (today + 30).
    [Fact]
    public void Default_start_date_is_inside_the_sale_window_and_is_neither_the_example_nor_a_boundary()
    {
        var start = Date(new SellPolicyRequestBuilder("GUARD").Build(), "startDate");

        Assert.NotEqual(ExampleStartDate, start);
        Assert.InRange(start, TestClock.Today.AddDays(1), TestClock.Today.AddDays(59));
        Assert.NotEqual(TestClock.Today.AddDays(15), start);
        Assert.NotEqual(TestClock.Today.AddDays(30), start);
    }

    [Fact]
    public void Default_amount_is_above_zero_and_is_not_the_example_amount()
    {
        var amount = new SellPolicyRequestBuilder("GUARD").Build()["amount"]!.GetValue<decimal>();

        Assert.NotEqual(ExampleAmount, amount);
        Assert.True(amount > 0m, $"The default amount {amount} is not a valid sale (SELL-7).");
    }

    [Fact]
    public void Default_address_is_not_the_example_address()
    {
        var property = new SellPolicyRequestBuilder("GUARD").Build()["property"]!.AsObject();

        Assert.NotEqual(ExampleAddressLine1, property["addressLine1"]!.GetValue<string>());
        Assert.NotEqual(ExamplePostcode, property["postcode"]!.GetValue<string>());
        Assert.NotNull(property["addressLine2"]);
    }

    private static DateOnly Date(JsonObject body, string property) =>
        DateOnly.ParseExact(body[property]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
