using System.Net;
using System.Text.Json;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1 returns the policy as stored, and hasClaims is one of its stored fields (Policy.HasClaims). No endpoint
// can make it true before F3's claims, so the policy is seeded through the app's IPolicyRepository
// (PolicyBuilder, PolicySeeder) and read back over HTTP (finding A2/I1). Every sold or seeded-default row
// elsewhere expects false, so this is the row a canned false fails. Refunds, claims and cancellation have no
// data source before F3, so they stay empty and null here.
// The seed is the PolicyBuilder defaults: BuyToLet, auto-renew, Mary Jackson born 1984-04-09, 31 Deansgate,
// Floor 4, M3 2BA, one term 2026-09-07 to 2027-09-06 for 446.50 paid by Card, in force on 2026-10-17.
public sealed class RetrieveHasClaimsTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly TermStart = new(2026, 9, 7);
    private static readonly DateOnly TermEnd = new(2027, 9, 6);

    private readonly PoliciesApiFactory _factory;

    public RetrieveHasClaimsTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Policy_stored_with_claims_reads_back_hasClaims_true_and_the_rest_as_stored()
    {
        const string reference = "GET1-HAS-CLAIMS";
        await _factory.Seed(new PolicyBuilder(reference).WithHasClaims(true).Build());
        using var client = _factory.CreateClient();

        using var read = await client.GetPolicy(reference);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var policy = await read.ReadJson();
        PolicyResponseAssert.HasTheContractShape(policy);
        Assert.True(policy.Member("hasClaims").GetBoolean());

        Assert.Equal(reference, policy.Member("reference").GetString());
        Assert.Equal("BuyToLet", policy.Member("type").GetString());
        Assert.Equal("Active", policy.Member("status").GetString());
        PolicyResponseAssert.ShowsCurrentTerm(policy, 1, TermStart, TermEnd, 446.50m);
        Assert.True(policy.Member("autoRenew").GetBoolean());

        var holder = Assert.Single(policy.Member("policyholders").EnumerateArray());
        Assert.Equal("Mary", holder.Member("firstName").GetString());
        Assert.Equal("Jackson", holder.Member("lastName").GetString());
        Assert.Equal(new DateOnly(1984, 4, 9), holder.GetDate("dateOfBirth"));

        var property = policy.Member("property");
        Assert.Equal("31 Deansgate", property.Member("addressLine1").GetString());
        Assert.Equal("Floor 4", property.Member("addressLine2").GetString());
        Assert.Equal(JsonValueKind.Null, property.Member("addressLine3").ValueKind);
        Assert.Equal("M3 2BA", property.Member("postcode").GetString());

        var term = Assert.Single(policy.Member("terms").EnumerateArray());
        Assert.Equal(1, term.Member("termNumber").GetInt32());
        Assert.Equal(TermStart, term.GetDate("startDate"));
        Assert.Equal(TermEnd, term.GetDate("endDate"));
        Assert.Equal(446.50m, term.Member("amount").GetDecimal());
        Assert.Equal("PAY-GET1-HAS-CLAIMS-1", term.Member("paymentReference").GetString());

        var payment = Assert.Single(policy.Member("payments").EnumerateArray());
        Assert.Equal("PAY-GET1-HAS-CLAIMS-1", payment.Member("reference").GetString());
        Assert.Equal("Card",payment.Member("type").GetString());
        Assert.Equal(446.50m, payment.Member("amount").GetDecimal());

        Assert.Empty(policy.Member("refunds").EnumerateArray());
        Assert.Empty(policy.Member("claims").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, policy.Member("cancellation").ValueKind);
    }
}
