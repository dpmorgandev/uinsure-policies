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

        var policy = await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL1-NEW").Build(), "SELL1-NEW");

        Assert.Equal("SELL1-NEW", policy.GetProperty("reference").GetString());
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_is_trimmed_and_upper_cased_in_the_Location_and_the_stored_policy()
    {
        using var client = _factory.CreateClient();

        var policy = await client.SellAndReadBack(new SellPolicyRequestBuilder("  sell1-norm  ").Build(), "SELL1-NORM");

        Assert.Equal("SELL1-NORM", policy.GetProperty("reference").GetString());
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Policy_sold_reads_back_by_its_reference_in_lower_case()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL1-LOWER-GET").Build(), "SELL1-LOWER-GET");

        using var read = await client.GetPolicy("sell1-lower-get");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("SELL1-LOWER-GET", (await read.ReadJson()).GetProperty("reference").GetString());
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_is_rejected_with_409_and_the_original_policy_is_unchanged()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL1-DUP").WithAmount(365.00m).Build(), "SELL1-DUP");

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-DUP").WithAmount(500.00m).Build());

        var problem = await ProblemAssert.Conflict(duplicate, "SELL-1", "Policy reference already exists");
        Assert.Equal("SELL1-DUP", problem.GetProperty("reference").GetString());
        using var read = await client.GetPolicy("SELL1-DUP");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(365.00m, (await read.ReadJson()).GetProperty("amount").GetDecimal());
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

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Reference_of_3_characters_is_accepted()
    {
        using var client = _factory.CreateClient();

        await client.SellAndReadBack(new SellPolicyRequestBuilder("S1A").Build(), "S1A");
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

        await client.SellAndReadBack(new SellPolicyRequestBuilder(twenty).Build(), twenty);
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
