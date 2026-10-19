using System.Text;
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

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-POSTCODE-8", new DateOnly(2027, 11, 7));
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

    // The name and address caps match their columns: 100 for a policyholder's first and last name, 200
    // for each address line. Each side of each cap is its own test.
    [Fact]
    public async Task First_name_of_100_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var firstName = new string('F', 100);
        var body = new SellPolicyRequestBuilder("SHAPE-FIRST-100")
            .WithPolicyholders(new PolicyholderBuilder().WithName(firstName, "Johnson"))
            .Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-FIRST-100");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-FIRST-100", new DateOnly(2027, 11, 7));
        var holder = Assert.Single(policy.Member("policyholders").EnumerateArray());
        Assert.Equal(firstName, holder.Member("firstName").GetString());
    }

    [Fact]
    public async Task First_name_of_101_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-FIRST-101")
            .WithPolicyholders(new PolicyholderBuilder().WithName(new string('F', 101), "Johnson"))
            .Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "policyholders[0].firstName");
        await client.AssertNotStored("SHAPE-FIRST-101");
    }

    [Fact]
    public async Task Last_name_of_100_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var lastName = new string('L', 100);
        var body = new SellPolicyRequestBuilder("SHAPE-LAST-100")
            .WithPolicyholders(new PolicyholderBuilder().WithName("Katherine", lastName))
            .Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-LAST-100");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-LAST-100", new DateOnly(2027, 11, 7));
        var holder = Assert.Single(policy.Member("policyholders").EnumerateArray());
        Assert.Equal(lastName, holder.Member("lastName").GetString());
    }

    [Fact]
    public async Task Last_name_of_101_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-LAST-101")
            .WithPolicyholders(new PolicyholderBuilder().WithName("Katherine", new string('L', 101)))
            .Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "policyholders[0].lastName");
        await client.AssertNotStored("SHAPE-LAST-101");
    }

    [Fact]
    public async Task Address_line_1_of_200_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var line = new string('A', 200);
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS1-200").WithAddress(line, "Unit 5", null).Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-ADDRESS1-200");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-ADDRESS1-200", new DateOnly(2027, 11, 7));
        Assert.Equal(line, policy.Member("property").Member("addressLine1").GetString());
    }

    [Fact]
    public async Task Address_line_1_of_201_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS1-201").WithAddress(new string('A', 201), "Unit 5", null).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "property.addressLine1");
        await client.AssertNotStored("SHAPE-ADDRESS1-201");
    }

    [Fact]
    public async Task Address_line_2_of_200_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var line = new string('B', 200);
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS2-200").WithAddress("48 Quay St", line, null).Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-ADDRESS2-200");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-ADDRESS2-200", new DateOnly(2027, 11, 7));
        Assert.Equal(line, policy.Member("property").Member("addressLine2").GetString());
    }

    [Fact]
    public async Task Address_line_2_of_201_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS2-201").WithAddress("48 Quay St", new string('B', 201), null).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "property.addressLine2");
        await client.AssertNotStored("SHAPE-ADDRESS2-201");
    }

    [Fact]
    public async Task Address_line_3_of_200_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var line = new string('C', 200);
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS3-200").WithAddress("48 Quay St", "Unit 5", line).Build();

        var policy = await client.SellAndReadBack(body, "SHAPE-ADDRESS3-200");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SHAPE-ADDRESS3-200", new DateOnly(2027, 11, 7));
        Assert.Equal(line, policy.Member("property").Member("addressLine3").GetString());
    }

    [Fact]
    public async Task Address_line_3_of_201_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-ADDRESS3-201").WithAddress("48 Quay St", "Unit 5", new string('C', 201)).Build();

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "property.addressLine3");
        await client.AssertNotStored("SHAPE-ADDRESS3-201");
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

    // The request is bound from the body, never the query string, so ?request= is an unbound query key and a
    // 400 even with a valid body (finding A2/I4), and nothing is stored.
    [Fact]
    public async Task Query_key_named_like_the_body_parameter_on_a_sale_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SHAPE-QUERY-REQUEST").Build();
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/policies?request=x", content, TestContext.Current.CancellationToken);

        var errors = await ProblemAssert.ValidationErrors(response);
        ProblemAssert.HasError(errors, "request");
        Assert.Equal("Unknown query parameter.", errors.Member("request")[0].GetString());
        await client.AssertNotStored("SHAPE-QUERY-REQUEST");
    }
}
