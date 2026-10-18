using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The concurrency row of the contract, through the records: a sold policy (POST → GET) saved from a stale copy
// answers 409 /problems/concurrency with no ruleId and a detail that asks for a retry, the stored policy reads
// back as sold, and the concurrency token is PolicyRecord.Version, never a Domain type's. The stale save is the
// factory's test path (F1 has no write endpoint that changes a stored policy).
// No Rule trait: concurrency is not a numbered rule.
public sealed class ConcurrencyConflictTests : IClassFixture<StaleSaveApiFactory>
{
    private const string ConcurrencyType = "/problems/concurrency";

    private readonly StaleSaveApiFactory _factory;

    public ConcurrencyConflictTests(StaleSaveApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Stale_save_of_a_sold_policy_answers_409_concurrency_with_no_ruleId()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(SellPolicyRequestBuilder.Distinctive("STALE-409").Build(), "STALE-409");

        using var response = await client.PostAsync(StaleSaveApiFactory.PathFor("STALE-409"), content: null, TestContext.Current.CancellationToken);

        var problem = await ProblemAssert.TransportProblem(response, HttpStatusCode.Conflict, StaleSaveApiFactory.Path, ConcurrencyType);
        Assert.Equal("The policy was changed by another request", problem.Member("title").GetString());
        Assert.Equal("Retry the request.", problem.Member("detail").GetString());
    }

    [Fact]
    public async Task Stale_save_of_a_sold_policy_leaves_it_reading_back_as_sold()
    {
        using var client = _factory.CreateClient();
        var sold = SellPolicyRequestBuilder.Distinctive("STALE-READ-BACK").Build();
        await client.SellAndReadBack(sold, "STALE-READ-BACK");

        using var response = await client.PostAsync(StaleSaveApiFactory.PathFor("STALE-READ-BACK"), content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var read = await client.GetPolicy("STALE-READ-BACK");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        SoldPolicyAssert.ReadsBackAsSold(await read.ReadJson(), sold, "STALE-READ-BACK", new DateOnly(2027, 11, 15));
    }

    [Fact]
    public async Task Stale_save_conflicts_on_the_Version_of_the_PolicyRecord()
    {
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(SellPolicyRequestBuilder.Distinctive("STALE-RECORD").Build(), "STALE-RECORD");

        using var response = await client.PostAsync(StaleSaveApiFactory.PathFor("STALE-RECORD"), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Uinsure.Policies.Persistence.Records.PolicyRecord", _factory.ConcurrencyTokenOwner);
    }
}
