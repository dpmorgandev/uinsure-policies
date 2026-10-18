using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1, the response shape: money is a JSON number in GBP written with exactly 2 decimal places, in the
// sale's 201 body and in the read-back alike, whatever scale the request used and however the store keeps
// it. The value is unchanged (527.180 is accepted by value, ruling H6); only the text is fixed. The raw JSON
// token is asserted, because a parsed number cannot tell 803.4 from 803.40. Each amount the PolicyResponse
// carries is checked: the policy's, its term's and its payment's.
public sealed class PolicyMoneyFormatTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public PolicyMoneyFormatTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Amount_sold_as_1000000_00_is_written_1000000_00_in_the_sale_and_the_read_back()
    {
        await AssertWrittenWithTwoDecimalPlaces("GET1-MONEY-1M", 1000000.00m, "1000000.00");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Amount_sold_as_803_40_keeps_its_trailing_zero_in_the_sale_and_the_read_back()
    {
        await AssertWrittenWithTwoDecimalPlaces("GET1-MONEY-803-40", 803.40m, "803.40");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Amount_sold_as_100_is_written_100_00_in_the_sale_and_the_read_back()
    {
        await AssertWrittenWithTwoDecimalPlaces("GET1-MONEY-100", 100m, "100.00");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Amount_sold_as_527_180_is_written_527_18_in_the_sale_and_the_read_back()
    {
        await AssertWrittenWithTwoDecimalPlaces("GET1-MONEY-527-180", 527.180m, "527.18");
    }

    // The writer pads and never rounds (finding A2/I3): a value with more than 2 decimal places is written
    // exactly, so a missing Money.Round upstream shows in a test instead of being hidden here. No request can
    // store such an amount (the 400 shape check), so the app's own configured serializer options, the ones
    // every controller response is written with, are asked directly.
    [Fact]
    [Trait("Rule", "GET-1")]
    public void Value_0_125_is_written_0_125_by_the_app_json_options_and_never_rounded()
    {
        Assert.Equal("0.125", JsonSerializer.Serialize(0.125m, AppJsonOptions()));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Value_803_4_is_padded_to_803_40_by_the_app_json_options()
    {
        Assert.Equal("803.40", JsonSerializer.Serialize(803.4m, AppJsonOptions()));
    }

    // The request carries the amount at the scale given (100 → "100", 527.180 → "527.180").
    private async Task AssertWrittenWithTwoDecimalPlaces(string reference, decimal amount, string expectedText)
    {
        using var client = _factory.CreateClient();
        var sold = new SellPolicyRequestBuilder(reference).WithAmount(amount).Build();

        using var created = await client.PostPolicy(sold);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.ReadJson();
        AssertMoneyText(createdBody, expectedText, "201 body");

        using var read = await client.GetAsync(created.LocationPath(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.ReadJson();
        AssertMoneyText(readBody, expectedText, "read-back");
        PolicyResponseAssert.ReadsBackAsSold(readBody, sold, reference, new DateOnly(2027, 11, 7), "Pending");
    }

    private JsonSerializerOptions AppJsonOptions() =>
        _factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    private static void AssertMoneyText(JsonElement policy, string expectedText, string where)
    {
        var term = Assert.Single(policy.Member("terms").EnumerateArray());
        var payment = Assert.Single(policy.Member("payments").EnumerateArray());

        Assert.Equal(
            (where, expectedText, expectedText, expectedText),
            (where, policy.Member("amount").GetRawText(), term.Member("amount").GetRawText(), payment.Member("amount").GetRawText()));
    }
}
