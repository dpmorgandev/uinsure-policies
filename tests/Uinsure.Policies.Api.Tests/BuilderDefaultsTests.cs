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
    private const string ExampleFirstName = "Ada";
    private const string ExampleLastName = "Lovelace";
    private static readonly DateOnly ExampleDateOfBirth = new(1985, 12, 10);

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

    [Fact]
    public void Default_policyholder_name_is_not_the_example_name()
    {
        var holder = new PolicyholderBuilder().Build();

        Assert.NotEqual(ExampleFirstName, holder["firstName"]!.GetValue<string>());
        Assert.NotEqual(ExampleLastName, holder["lastName"]!.GetValue<string>());
    }

    // The default holder must stay 16 or older (SELL-5) on every start a default-holder test sells,
    // today + 61 (the multi-violation row) and 2028-02-29 (the SELL-3 29 Feb row) the latest of them.
    [Fact]
    public void Default_policyholder_date_of_birth_is_not_the_example_and_is_16_or_older_on_every_sold_start()
    {
        var dateOfBirth = Date(new PolicyholderBuilder().Build(), "dateOfBirth");

        Assert.NotEqual(ExampleDateOfBirth, dateOfBirth);
        Assert.True(dateOfBirth <= TestClock.Today.AddDays(61).AddYears(-16), "The default holder is under 16 on today + 61.");
        Assert.True(dateOfBirth <= new DateOnly(2028, 2, 29).AddYears(-16), "The default holder is under 16 on 2028-02-29.");
    }

    // The sale defaults carry the default holder, alone.
    [Fact]
    public void Default_sale_carries_one_policyholder_who_is_not_the_example_holder()
    {
        var holder = Assert.Single(new SellPolicyRequestBuilder("GUARD").Build()["policyholders"]!.AsArray())!.AsObject();

        Assert.NotEqual(
            (ExampleFirstName, ExampleLastName, ExampleDateOfBirth),
            (holder["firstName"]!.GetValue<string>(), holder["lastName"]!.GetValue<string>(), Date(holder, "dateOfBirth")));
    }

    private static DateOnly Date(JsonObject body, string property) =>
        DateOnly.ParseExact(body[property]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
