using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-1: the client's reference, trimmed and upper-cased, must match ^[A-Z0-9-]{3,20}$ (else 400) and be
// unique (else 409). abc-1 and ABC-1 are the same policy.
public sealed class SellReferenceTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellReferenceTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task New_reference_is_sold_with_201_and_its_Location_and_reads_back()
    {
        using var client = _factory.CreateClient();
        var body = SellPolicyRequestBuilder.Distinctive("SELL1-NEW").Build();

        var policy = await client.SellAndReadBack(body, "SELL1-NEW");

        Assert.Equal("SELL1-NEW", policy.GetProperty("reference").GetString());
        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL1-NEW", new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_is_trimmed_and_upper_cased_in_the_Location_and_the_stored_policy()
    {
        using var client = _factory.CreateClient();
        var body = SellPolicyRequestBuilder.Distinctive("  sell1-norm  ").Build();

        var policy = await client.SellAndReadBack(body, "SELL1-NORM");

        Assert.Equal("SELL1-NORM", policy.GetProperty("reference").GetString());
        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL1-NORM", new DateOnly(2027, 11, 15));
    }

    // The lower-case GET must find the stored row: it reads back the distinctive sold values, not only
    // the reference, which a route echo could produce.
    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Policy_sold_reads_back_by_its_reference_in_lower_case()
    {
        using var client = _factory.CreateClient();
        var body = SellPolicyRequestBuilder.Distinctive("SELL1-LOWER-GET").Build();
        var policy = await client.SellAndReadBack(body, "SELL1-LOWER-GET");
        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL1-LOWER-GET", new DateOnly(2027, 11, 15));

        using var read = await client.GetPolicy("sell1-lower-get");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readByLowerCase = await read.ReadJson();
        Assert.Equal("SELL1-LOWER-GET", readByLowerCase.GetProperty("reference").GetString());
        SoldPolicyAssert.ReadsBackAsSold(readByLowerCase, body, "SELL1-LOWER-GET", new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_is_rejected_with_409_and_the_original_policy_is_unchanged()
    {
        using var client = _factory.CreateClient();
        var original = SellPolicyRequestBuilder.Distinctive("SELL1-DUP").WithAmount(412.34m).Build();
        var sold = await client.SellAndReadBack(original, "SELL1-DUP");
        SoldPolicyAssert.ReadsBackAsSold(sold, original, "SELL1-DUP", new DateOnly(2027, 11, 15));

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-DUP").WithAmount(500.00m).Build());

        var problem = await ProblemAssert.Conflict(duplicate, "SELL-1", "Policy reference already exists");
        Assert.Equal("SELL1-DUP", problem.GetProperty("reference").GetString());
        using var read = await client.GetPolicy("SELL1-DUP");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var afterDuplicate = await read.ReadJson();
        Assert.Equal(412.34m, afterDuplicate.GetProperty("amount").GetDecimal());
        SoldPolicyAssert.ReadsBackAsSold(afterDuplicate, original, "SELL1-DUP", new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_differing_only_in_case_is_rejected_with_409()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL1-CASE").Build(), "SELL1-CASE");

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder("sell1-case").Build());

        var problem = await ProblemAssert.Conflict(duplicate, "SELL-1", "Policy reference already exists");
        Assert.Equal("SELL1-CASE", problem.GetProperty("reference").GetString());
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_differing_only_in_surrounding_spaces_is_rejected_with_409()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL1-SPACES").Build(), "SELL1-SPACES");

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder(" SELL1-SPACES ").Build());

        var problem = await ProblemAssert.Conflict(duplicate, "SELL-1", "Policy reference already exists");
        Assert.Equal("SELL1-SPACES", problem.GetProperty("reference").GetString());
    }

    // A 409 short-circuits before the 422 rules: the duplicate also starts on today + 61 (SELL-2) for an
    // amount of 0 (SELL-7), and still gets only SELL-1, with no violations[].
    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_with_a_body_that_breaks_SELL_2_and_SELL_7_is_rejected_with_409_alone()
    {
        using var client = _factory.CreateClient();
        var original = SellPolicyRequestBuilder.Distinctive("SELL1-DUP-422").Build();
        var sold = await client.SellAndReadBack(original, "SELL1-DUP-422");
        SoldPolicyAssert.ReadsBackAsSold(sold, original, "SELL1-DUP-422", new DateOnly(2027, 11, 15));

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-DUP-422")
            .WithStartDate(TestClock.Today.AddDays(61))
            .WithAmount(0.00m)
            .Build());

        var problem = await ProblemAssert.Conflict(duplicate, "SELL-1", "Policy reference already exists");
        Assert.Equal("SELL1-DUP-422", problem.GetProperty("reference").GetString());
        using var read = await client.GetPolicy("SELL1-DUP-422");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        SoldPolicyAssert.ReadsBackAsSold(await read.ReadJson(), original, "SELL1-DUP-422", new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_3_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = SellPolicyRequestBuilder.Distinctive("S1A").Build();

        var policy = await client.SellAndReadBack(body, "S1A");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "S1A", new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_2_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostPolicy(new SellPolicyRequestBuilder("S1").Build());

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "reference");
        await client.AssertNotStored("S1");
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_20_characters_is_accepted()
    {
        using var client = _factory.CreateClient();
        const string twenty = "SELL1-MAX-LEN-20CHAR";
        Assert.Equal(20, twenty.Length);
        var body = SellPolicyRequestBuilder.Distinctive(twenty).Build();

        var policy = await client.SellAndReadBack(body, twenty);

        SoldPolicyAssert.ReadsBackAsSold(policy, body, twenty, new DateOnly(2027, 11, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_21_characters_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        const string twentyOne = "SELL1-MAX-LEN-21CHARS";
        Assert.Equal(21, twentyOne.Length);

        using var response = await client.PostPolicy(new SellPolicyRequestBuilder(twentyOne).Build());

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "reference");
        await client.AssertNotStored(twentyOne);
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_2_characters_once_trimmed_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostPolicy(new SellPolicyRequestBuilder("  S2  ").Build());

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "reference");
        await client.AssertNotStored("S2");
    }

    // Each is a character outside A-Z, 0-9 and hyphen once upper-cased: equivalent inputs.
    [Theory]
    [Trait("Rule", "SELL-1")]
    [InlineData("SELL1_UNDERSCORE")]
    [InlineData("SELL1 INNER-SPACE")]
    [InlineData("SELL1.DOT")]
    [InlineData("SELL1/SLASH")]
    public async Task Reference_with_a_character_outside_A_Z_0_9_and_hyphen_is_rejected_with_400(string reference)
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostPolicy(new SellPolicyRequestBuilder(reference).Build());

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "reference");
        await client.AssertNotStored(reference);
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Missing_reference_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL1-MISSING").Build();
        body.Remove("reference");

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "reference");
        await client.AssertNotStored("SELL1-MISSING");
    }
}
