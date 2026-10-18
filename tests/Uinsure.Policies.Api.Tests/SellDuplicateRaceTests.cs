using System.Net;
using System.Text.Json;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-1: a unique DB index enforces the reference too. Two sales of one reference can both pass the
// service's duplicate check; the one that saves second hits the index and must get the same 409 SELL-1
// as the service check gives, and the stored policy is the first sale's.
public sealed class SellDuplicateRaceTests : IClassFixture<DuplicateRaceApiFactory>
{
    private const string Title = "Policy reference already exists";

    private readonly DuplicateRaceApiFactory _factory;

    public SellDuplicateRaceTests(DuplicateRaceApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_that_races_past_the_service_check_is_rejected_with_409_by_the_unique_index_and_the_original_is_unchanged()
    {
        using var client = _factory.CreateClient();
        var original = SellPolicyRequestBuilder.Distinctive("SELL1-RACE").Build();
        var sold = await client.SellAndReadBack(original, "SELL1-RACE");
        SoldPolicyAssert.ReadsBackAsSold(sold, original, "SELL1-RACE", new DateOnly(2027, 11, 15));
        _factory.HideOnceFromDuplicateCheck("SELL1-RACE");

        using var duplicate = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-RACE").Build());

        await ProblemAssert.Conflict(duplicate, "SELL-1", Title);
        Assert.Equal(1, _factory.HiddenLookups("SELL1-RACE"));
        using var read = await client.GetPolicy("SELL1-RACE");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        SoldPolicyAssert.ReadsBackAsSold(await read.ReadJson(), original, "SELL1-RACE", new DateOnly(2027, 11, 15));
    }

    // The race-path body is the service-path body: the contract's detail with the reference, the reference
    // extension, and every other member equal, bar the per-request traceId.
    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_that_races_past_the_service_check_gets_the_same_SELL_1_body_as_the_service_check()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(SellPolicyRequestBuilder.Distinctive("SELL1-RACE-BODY").Build(), "SELL1-RACE-BODY");
        _factory.HideOnceFromDuplicateCheck("SELL1-RACE-BODY");

        using var raced = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-RACE-BODY").Build());
        var racedProblem = await ProblemAssert.Conflict(raced, "SELL-1", Title);
        Assert.Equal(1, _factory.HiddenLookups("SELL1-RACE-BODY"));

        Assert.Equal("A policy with reference 'SELL1-RACE-BODY' already exists.", racedProblem.Member("detail").GetString());
        Assert.Equal("SELL1-RACE-BODY", racedProblem.Member("reference").GetString());

        using var checkedDuplicate = await client.PostPolicy(new SellPolicyRequestBuilder("SELL1-RACE-BODY").Build());
        var checkedProblem = await ProblemAssert.Conflict(checkedDuplicate, "SELL-1", Title);
        Assert.Equal(MembersExceptTraceId(checkedProblem), MembersExceptTraceId(racedProblem));
    }

    private static SortedDictionary<string, string> MembersExceptTraceId(JsonElement problem) =>
        new(problem.EnumerateObject()
            .Where(member => member.Name != "traceId")
            .ToDictionary(member => member.Name, member => member.Value.GetRawText()), StringComparer.Ordinal);
}
