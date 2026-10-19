using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// COULD-1 at the service: PolicyService.Quote is the cancel calculation, read only. It loads the
// policy, reads UK today once, asks policy.Quote, and saves nothing: no SaveChanges, no Add, no
// refund reference, and the policy stays uncancelled.
//   The default stored term runs 2026-11-08 to 2027-11-07 (365 days), 527.18. A term starting 2027-07-01 runs
//   to 2028-06-30 (366 days). At 23:30 UTC on 30 June 2027 (BST) the UK date is already 1 July.
public sealed class PolicyServiceQuoteTests
{
    private static readonly DateTimeOffset BstLateEvening = TestClock.Utc(2027, 6, 30, 23, 30);

    private readonly SequentialReferenceGenerator _references = new();

    // Today is 2026-10-17, before the 2026-11-08 start: a full refund under CANCEL-1a.
    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_computes_the_refund_without_saving()
    {
        var policy = new StoredPolicyBuilder("Q1-SVC-NOSAVE").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await QuoteAt(TestClock.Now, repository, policy.Reference, null);

        Assert.True(result.IsSuccess, "The quote was expected to succeed.");
        Assert.Equal([new RefundCalculation(527.18m, "CANCEL-1a", 365, 0, 365)], result.Value.Quote.Terms);
        AssertNothingChanged(policy, repository);
    }

    // The UK date is the start date, so cooling-off (CANCEL-1b) applies; the UTC date would be the day before
    // the start (CANCEL-1a, no day used).
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Quote_with_no_date_at_23_30_UTC_in_BST_is_for_the_next_UK_date()
    {
        var policy = new StoredPolicyBuilder("Q1-SVC-BST-DEF").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await QuoteAt(BstLateEvening, repository, policy.Reference, null);

        Assert.True(result.IsSuccess, "The quote was expected to succeed.");
        Assert.Equal([new RefundCalculation(527.18m, "CANCEL-1b", 366, 1, 365)], result.Value.Quote.Terms);
        AssertNothingChanged(policy, repository);
    }

    // 2027-06-30 is the UTC date, but the UK date is already 1 July: the quote applies CANCEL-3 as the
    // cancellation would.
    [Fact]
    [Trait("Rule", "COULD-1")]
    [Trait("Rule", "CANCEL-3")]
    public async Task Quote_for_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past_and_saves_nothing()
    {
        var policy = new StoredPolicyBuilder("Q1-SVC-BST-UTC").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await QuoteAt(BstLateEvening, repository, policy.Reference, new DateOnly(2027, 6, 30));

        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("CANCEL-3", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.NotNull(violation.Extensions);
        Assert.Equal(new DateOnly(2027, 7, 1), violation.Extensions["today"]);
        AssertNothingChanged(policy, repository);
    }

    // 2026-11-23 is start + 15: 527.18 × 349 ÷ 365 = 504.0707… → 504.07, in the quote and in the cancellation.
    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_is_the_calculation_the_cancellation_for_the_same_date_then_applies()
    {
        var policy = new StoredPolicyBuilder("Q1-SVC-SAME").Build();
        var repository = new StoredPolicyRepository(policy);
        var service = new PolicyService(repository, _references, new FakeTimeProvider(TestClock.Now));

        var quote = await service.Quote(policy.Reference, new DateOnly(2026, 11, 23), TestContext.Current.CancellationToken);
        Assert.Equal(0, repository.SaveChangesCalls);
        var cancellation = await service.Cancel(policy.Reference, new DateOnly(2026, 11, 23), TestContext.Current.CancellationToken);

        Assert.True(quote.IsSuccess, "The quote was expected to succeed.");
        Assert.True(cancellation.IsSuccess, "The cancellation was expected to succeed.");
        Assert.Equal([new RefundCalculation(504.07m, "CANCEL-1c", 365, 16, 349)], quote.Value.Quote.Terms);
        Assert.Equal(quote.Value.Quote.Terms, cancellation.Value.Terms);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    // Today is computed once per command, so the default date and the past-date check agree on one date.
    [Fact]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_reads_the_clock_once()
    {
        var policy = new StoredPolicyBuilder("Q1-SVC-ONCE").Build();
        var clock = new CountingTimeProvider(TestClock.Now);
        var service = new PolicyService(new StoredPolicyRepository(policy), _references, clock);

        var result = await service.Quote(policy.Reference, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The quote was expected to succeed.");
        Assert.Equal(1, clock.UtcNowReads);
    }

    private Task<Result<CancellationQuoteResult>> QuoteAt(DateTimeOffset utcNow, StoredPolicyRepository repository, PolicyReference reference, DateOnly? on) =>
        new PolicyService(repository, _references, new FakeTimeProvider(utcNow)).Quote(reference, on, TestContext.Current.CancellationToken);

    private void AssertNothingChanged(Policy policy, StoredPolicyRepository repository)
    {
        Assert.Null(policy.Cancellation);
        Assert.Equal(0, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, _references.RefundCalls);
    }
}
