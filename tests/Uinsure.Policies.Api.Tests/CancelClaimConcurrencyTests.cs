using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The concurrency row of the contract on the F3 writes (E3, E5): when another request saves the same policy
// between this request's load and its save, the PolicyRecord's Version no longer matches, and the request
// answers 409 /problems/concurrency, with no ruleId and a detail that asks for a retry. Nothing it would
// have written is stored. The race is made by ConcurrentWriteApiFactory at exactly that point.
// No Rule trait: concurrency is not a numbered rule.
//   start 2026-10-17 ends 2027-10-16.
public sealed class CancelClaimConcurrencyTests : IClassFixture<ConcurrentWriteApiFactory>
{
    private const string ConcurrencyType = "/problems/concurrency";

    private readonly ConcurrentWriteApiFactory _factory;

    public CancelClaimConcurrencyTests(ConcurrentWriteApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Cancellation_that_loses_a_race_answers_409_concurrency_and_stores_nothing()
    {
        const string reference = "RACE-CANCEL";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(353.20m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.LoseTheNextSaveOf(reference);

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 1, 10)).Build());

        var problem = await ProblemAssert.TransportProblem(response, HttpStatusCode.Conflict, PolicyActions.CancellationPath(reference), ConcurrencyType);
        Assert.Equal("The policy was changed by another request", problem.Member("title").GetString());
        Assert.Equal("Retry the request.", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Active");
    }

    [Fact]
    public async Task Claim_that_loses_a_race_answers_409_concurrency_and_stores_nothing()
    {
        const string reference = "RACE-CLAIM";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(296.41m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.LoseTheNextSaveOf(reference);

        using var response = await client.PostClaim(reference, new ClaimRequestBuilder().Build());

        var problem = await ProblemAssert.TransportProblem(response, HttpStatusCode.Conflict, PolicyActions.ClaimsPath(reference), ConcurrencyType);
        Assert.Equal("The policy was changed by another request", problem.Member("title").GetString());
        Assert.Equal("Retry the request.", problem.Member("detail").GetString());
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Active");
    }

    // After the lost race the policy can be cancelled on a retry: the conflict left nothing half-written.
    [Fact]
    public async Task Cancellation_retried_after_a_lost_race_is_accepted()
    {
        const string reference = "RACE-RETRY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(353.20m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var paymentReference = (await client.SellAndReadBack(sold, reference)).PaymentReference();
        _factory.LoseTheNextSaveOf(reference);
        using (var lost = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 1, 10)).Build()))
        {
            Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        }

        // 2027-01-10: 86 days used, 279 unused; 353.20 × 279 ÷ 365 = 269.98027… → 269.98.
        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 1, 10)).Build());

        var refundReferences = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, new DateOnly(2027, 1, 10), "CANCEL-1c", "269.98",
            [new ExpectedTerm(1, "CANCEL-1c", 365, 86, 279, "269.98")],
            [new ExpectedRefund("Card", "269.98", paymentReference)]));
        PolicyStateAssert.ReadsBack(
            await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Cancelled", false,
            new ExpectedCancellationSummary(new DateOnly(2027, 1, 10), "CANCEL-1c", "269.98"),
            [new ExpectedPolicyRefund(Assert.Single(refundReferences), "Card", "269.98", paymentReference)],
            []);
    }
}
