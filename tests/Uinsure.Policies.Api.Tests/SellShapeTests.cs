using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The 400 shape rows of E1: strict input is rejected before any rule runs, as ValidationProblemDetails
// with an errors map and no ruleId. Shape is not a numbered rule, so these carry no Rule trait.
// The reference-pattern and end-date rows live with SELL-1 and SELL-3, the missing payment method with SELL-6.
public sealed class SellShapeTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellShapeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Malformed_JSON_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostPolicy("{\"reference\": \"SHAPE-MALFORMED\", \"type\": \"Household\",");

        await ProblemAssert.ValidationErrors(response);
        await client.AssertNotStored("SHAPE-MALFORMED");
    }

    [Fact]
    public async Task Unknown_JSON_field_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-UNKNOWN-FIELD").Build();
        body["colour"] = "red";

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.colour");
        await client.AssertNotStored("SHAPE-UNKNOWN-FIELD");
    }

    // HasClaims is false at sale; the client can't set it.
    [Fact]
    public async Task Has_claims_sent_at_sale_is_rejected_with_400_as_an_unknown_field()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-HAS-CLAIMS").Build();
        body["hasClaims"] = false;

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.hasClaims");
        await client.AssertNotStored("SHAPE-HAS-CLAIMS");
    }

    [Fact]
    public async Task Unknown_field_on_the_property_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-PROPERTY-FIELD").Build();
        body["property"]!["county"] = "Lancashire";

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.property.county");
        await client.AssertNotStored("SHAPE-PROPERTY-FIELD");
    }

    // Each is a required top-level member of SellPolicyRequest: equivalent inputs.
    [Theory]
    [InlineData("type", "SHAPE-NO-TYPE")]
    [InlineData("startDate", "SHAPE-NO-START")]
    [InlineData("amount", "SHAPE-NO-AMOUNT")]
    [InlineData("autoRenew", "SHAPE-NO-AUTO-RENEW")]
    [InlineData("policyholders", "SHAPE-NO-HOLDERS")]
    [InlineData("property", "SHAPE-NO-PROPERTY")]
    public async Task Missing_required_field_is_rejected_with_400(string field, string reference)
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder(reference).Build();
        body.Remove(field);

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), field);
        await client.AssertNotStored(reference);
    }

    [Fact]
    public async Task Null_start_date_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-NULL-START").Build();
        body["startDate"] = null;

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "startDate");
        await client.AssertNotStored("SHAPE-NULL-START");
    }

    [Fact]
    public async Task Start_date_not_in_yyyy_MM_dd_format_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-START-FORMAT").Build();
        body["startDate"] = "01/11/2026";

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.startDate");
        await client.AssertNotStored("SHAPE-START-FORMAT");
    }

    [Fact]
    public async Task Amount_sent_as_a_string_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-AMOUNT-STRING").Build();
        body["amount"] = "365.00";

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.amount");
        await client.AssertNotStored("SHAPE-AMOUNT-STRING");
    }

    [Fact]
    public async Task Unknown_policy_type_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-TYPE-UNKNOWN").WithType("Flat").Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.type");
        await client.AssertNotStored("SHAPE-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Integer_policy_type_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-TYPE-INTEGER").Build();
        body["type"] = 0;

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.type");
        await client.AssertNotStored("SHAPE-TYPE-INTEGER");
    }

    [Fact]
    public async Task Unknown_payment_method_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-METHOD-UNKNOWN").WithPaymentMethod("Bitcoin").Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.paymentMethod");
        await client.AssertNotStored("SHAPE-METHOD-UNKNOWN");
    }

    [Fact]
    public async Task Integer_payment_method_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-METHOD-INTEGER").Build();
        body["paymentMethod"] = 1;

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.paymentMethod");
        await client.AssertNotStored("SHAPE-METHOD-INTEGER");
    }

    [Fact]
    public async Task Postcode_of_8_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        const string eight = "SW1A 1AA";
        Assert.Equal(8, eight.Length);
        var body = new SellPolicyRequestBuilder("SHAPE-POSTCODE-8").WithPostcode(eight).Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-POSTCODE-8");

        Assert.Equal(eight, policy.GetProperty("property").GetProperty("postcode").GetString());
    }

    [Fact]
    public async Task Postcode_of_9_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        const string nine = "SW1A 1AAA";
        Assert.Equal(9, nine.Length);
        var body = new SellPolicyRequestBuilder("SHAPE-POSTCODE-9").WithPostcode(nine).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "property.postcode");
        await client.AssertNotStored("SHAPE-POSTCODE-9");
    }

    // Each is a required member of the property: equivalent inputs.
    [Theory]
    [InlineData("addressLine1", "SHAPE-NO-ADDRESS-1")]
    [InlineData("postcode", "SHAPE-NO-POSTCODE")]
    public async Task Missing_required_property_field_is_rejected_with_400(string field, string reference)
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder(reference).Build();
        body["property"]!.AsObject().Remove(field);

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), $"property.{field}");
        await client.AssertNotStored(reference);
    }

    // Each is a required member of a policyholder: equivalent inputs.
    [Theory]
    [InlineData("firstName", "SHAPE-NO-FIRST-NAME")]
    [InlineData("lastName", "SHAPE-NO-LAST-NAME")]
    [InlineData("dateOfBirth", "SHAPE-NO-DOB")]
    public async Task Missing_required_policyholder_field_is_rejected_with_400(string field, string reference)
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder(reference).Build();
        body["policyholders"]![0]!.AsObject().Remove(field);

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), $"policyholders[0].{field}");
        await client.AssertNotStored(reference);
    }
}
