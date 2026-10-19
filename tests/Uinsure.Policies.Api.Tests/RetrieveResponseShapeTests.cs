using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1: the full PolicyResponse of the API contract. A sold policy reads back with every value it was sold
// with and the members F2 adds (status, currentTermNumber, hasClaims, refunds, claims, cancellation), and
// no member outside the contract. The sale's 201 body is the same PolicyResponse. Distinctive sells values
// that are neither the builder defaults nor the api.md example; it starts on 2026-11-16 and ends on 2027-11-15.
public sealed class RetrieveResponseShapeTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly DistinctiveEnd = new(2027, 11, 15);

    private readonly PoliciesApiFactory _factory;

    public RetrieveResponseShapeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Sold_policy_reads_back_in_full_with_status_current_term_and_empty_history()
    {
        using var client = _factory.CreateClient();
        var sold = SellPolicyRequestBuilder.Distinctive("GET1-FULL").Build();

        var policy = await client.SellAndReadBack(sold, "GET1-FULL");

        PolicyResponseAssert.ReadsBackAsSold(policy, sold, "GET1-FULL", DistinctiveEnd, "Pending");
        PolicyResponseAssert.ShowsCurrentTerm(policy, 1, new DateOnly(2026, 11, 16), DistinctiveEnd, 412.34m);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Sale_201_body_is_the_full_policy_response_and_equals_the_read_back()
    {
        using var client = _factory.CreateClient();
        var sold = SellPolicyRequestBuilder.Distinctive("GET1-POST-BODY").Build();

        using var created = await client.PostPolicy(sold);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.ReadJson();
        using var read = await client.GetAsync(created.LocationPath(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.ReadJson();

        PolicyResponseAssert.ReadsBackAsSold(createdBody, sold, "GET1-POST-BODY", DistinctiveEnd, "Pending");
        Assert.Equal(createdBody.GetRawText(), readBody.GetRawText());
    }

    // The route reference is trimmed and upper-cased before the lookup.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Lower_case_route_reference_with_spaces_reads_back_the_policy_in_full()
    {
        using var client = _factory.CreateClient();
        var sold = SellPolicyRequestBuilder.Distinctive("GET1-LOWER").Build();
        await client.SellAndReadBack(sold, "GET1-LOWER");

        using var read = await client.GetPolicy(" get1-lower ");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        PolicyResponseAssert.ReadsBackAsSold(await read.ReadJson(), sold, "GET1-LOWER", DistinctiveEnd, "Pending");
    }
}
