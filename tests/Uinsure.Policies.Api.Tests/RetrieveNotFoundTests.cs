using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1: a reference with no policy is a 404 /problems/get-1 naming the reference, after the route reference
// is trimmed and upper-cased. A reference that fails the SELL-1 pattern is not a 400 on a GET: no such policy
// can exist, so it is the same 404 (api.md conventions). The instance is the request path as sent.
public sealed class RetrieveNotFoundTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public RetrieveNotFoundTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Unknown_reference_answers_404_get_1_naming_the_reference()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetPolicy("GET1-UNKNOWN");

        await ProblemAssert.NotFound(response, "/policies/GET1-UNKNOWN", "GET1-UNKNOWN");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Unknown_lower_case_reference_answers_404_naming_the_normalised_reference()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetPolicy("get1-unknown-lc");

        await ProblemAssert.NotFound(response, "/policies/get1-unknown-lc", "GET1-UNKNOWN-LC");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Reference_that_fails_the_sell_1_pattern_answers_404_not_400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetPolicy("GET1_BAD");

        await ProblemAssert.NotFound(response, "/policies/GET1_BAD", "GET1_BAD");
    }

    // The 404 is for the reference asked for, not any reference: a sold policy next to it changes nothing.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Reference_one_character_longer_than_a_sold_one_answers_404()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("GET1-NEAR").Build(), "GET1-NEAR");

        using var response = await client.GetPolicy("GET1-NEARX");

        await ProblemAssert.NotFound(response, "/policies/GET1-NEARX", "GET1-NEARX");
    }

    // Shape row of the contract for E2: an unbound query key is a 400 with errors[key], before the lookup,
    // even for a policy that exists. Shape is not a numbered rule, so no Rule trait.
    [Fact]
    public async Task Unknown_query_parameter_on_a_get_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(new SellPolicyRequestBuilder("GET-QUERY").Build(), "GET-QUERY");

        using var response = await client.GetAsync("/policies/GET-QUERY?colour=red", TestContext.Current.CancellationToken);

        var errors = await ProblemAssert.ValidationErrors(response, "/policies/GET-QUERY");
        ProblemAssert.HasError(errors, "colour");
        Assert.Equal("Unknown query parameter.", errors.Member("colour")[0].GetString());
    }
}
