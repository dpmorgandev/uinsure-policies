using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The concurrency row of the contract on E6: when another request saves the same policy between this renewal's
// load and its save, the PolicyRecord's Version no longer matches, and the renewal answers 409
// /problems/concurrency, with no ruleId and a detail that asks for a retry. No term or payment it would have
// added is stored, and a retry is accepted. The race is made by ConcurrentWriteApiFactory at exactly that point.
// No Rule trait: concurrency is not a numbered rule.
//   start 2026-11-15 ends 2027-11-14, renewed on 2027-11-01; term 2 2027-11-15 to 2028-11-14.
public sealed class RenewConcurrencyTests : IClassFixture<ConcurrentWriteApiFactory>
{
    private const string ConcurrencyType = "/problems/concurrency";

    private readonly ConcurrentWriteApiFactory _factory;

    public RenewConcurrencyTests(ConcurrentWriteApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Renewal_that_loses_a_race_answers_409_concurrency_and_stores_nothing()
    {
        const string reference = "RACE-RENEW";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 15)).WithAmount(421.70m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 1));
        _factory.LoseTheNextSaveOf(reference);

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(445.90m).Build());

        var problem = await ProblemAssert.TransportProblem(response, HttpStatusCode.Conflict, RenewalActions.RenewalsPath(reference), ConcurrencyType);
        Assert.Equal("The policy was changed by another request", problem.Member("title").GetString());
        Assert.Equal("Retry the request.", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 14), "Active");
    }

    [Fact]
    public async Task Renewal_retried_after_a_lost_race_is_accepted()
    {
        const string reference = "RACE-RENEW-RETRY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 15)).WithAmount(421.70m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 1));
        _factory.LoseTheNextSaveOf(reference);
        using (var lost = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(445.90m).Build()))
        {
            Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        }

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(445.90m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 11, 15), new DateOnly(2027, 11, 14), "421.70", "DirectDebit"),
                new ExpectedPolicyTerm(new DateOnly(2027, 11, 15), new DateOnly(2028, 11, 14), "445.90", "DirectDebit"),
            ]),
            [sale.PaymentReference()]);
    }
}
