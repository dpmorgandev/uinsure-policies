using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// CANCEL-3 at the layer that knows the clock: PolicyService.Cancel reads today once, in Europe/London, and hands
// it to policy.Cancel. With no date the policy is cancelled on UK today; a date before UK today is 422 and
// nothing is saved. The domain's own boundaries are proven in Domain.Tests.
//   At 23:30 UTC on 30 June 2027 (BST) the UK date is already 1 July; at 23:30 UTC on 15 January 2027 (GMT) it
//   is still 15 January.
//   The default stored term runs 2026-11-08 to 2027-11-07 (365 days), 527.18. A term starting 2027-07-01 runs
//   to 2028-06-30 (366 days, through 29 Feb 2028), 527.18.
public sealed class PolicyServiceCancelDateTests
{
    private static readonly DateTimeOffset BstLateEvening = TestClock.Utc(2027, 6, 30, 23, 30);
    private static readonly DateTimeOffset GmtLateEvening = TestClock.Utc(2027, 1, 15, 23, 30);

    private readonly SequentialReferenceGenerator _references = new();

    // Today is 2026-10-17, before the 2026-11-08 start: a full refund under CANCEL-1a.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_no_date_cancels_on_UK_today()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-DEFAULT").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(TestClock.Now, repository, policy.Reference, null);

        var cancellation = AssertCancelledAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2026, 10, 17), cancellation.CancellationDate);
        Assert.Equal([new RefundCalculation(527.18m, "CANCEL-1a", 365, 0, 365)], cancellation.Terms);
    }

    // The UK date is the start date, so cooling-off (CANCEL-1b) applies; the UTC date would be the day before
    // the start (CANCEL-1a, no day used).
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_no_date_at_23_30_UTC_in_BST_cancels_on_the_next_UK_date()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-BST-DEF").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(BstLateEvening, repository, policy.Reference, null);

        var cancellation = AssertCancelledAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 7, 1), cancellation.CancellationDate);
        Assert.Equal("CANCEL-1b", cancellation.RuleApplied);
        Assert.Equal([new RefundCalculation(527.18m, "CANCEL-1b", 366, 1, 365)], cancellation.Terms);
    }

    // 2027-01-15 is day 69 of the 2026-11-08 term: 527.18 × 296 ÷ 365 = 427.5213… → 427.52.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_no_date_at_23_30_UTC_in_GMT_cancels_on_the_same_UK_date()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-GMT-DEF").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(GmtLateEvening, repository, policy.Reference, null);

        var cancellation = AssertCancelledAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 1, 15), cancellation.CancellationDate);
        Assert.Equal([new RefundCalculation(427.52m, "CANCEL-1c", 365, 69, 296)], cancellation.Terms);
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-BST-0").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(BstLateEvening, repository, policy.Reference, new DateOnly(2027, 7, 1));

        var cancellation = AssertCancelledAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 7, 1), cancellation.CancellationDate);
    }

    // 2027-06-30 is before the 2027-07-01 start and within cover, so only CANCEL-3 can reject it: it is the
    // UTC date, but the UK date is already 1 July.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-BST-UTC").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(BstLateEvening, repository, policy.Reference, new DateOnly(2027, 6, 30));

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 7, 1), Extension(violation, "today"));
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_of_the_UK_date_at_23_30_UTC_in_GMT_is_accepted()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-GMT-0").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(GmtLateEvening, repository, policy.Reference, new DateOnly(2027, 1, 15));

        var cancellation = AssertCancelledAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 1, 15), cancellation.CancellationDate);
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancellation_date_the_day_before_the_UK_date_at_23_30_UTC_in_GMT_is_rejected_as_past()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-GMT-YDAY").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await CancelAt(GmtLateEvening, repository, policy.Reference, new DateOnly(2027, 1, 14));

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 1, 15), Extension(violation, "today"));
    }

    // Today is computed once per command, so the default date and the past-date check agree on one date.
    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_reads_the_clock_once()
    {
        var policy = new StoredPolicyBuilder("C3-SVC-ONCE").Build();
        var clock = new CountingTimeProvider(TestClock.Now);
        var service = new PolicyService(new StoredPolicyRepository(policy), _references, clock);

        var result = await service.Cancel(policy.Reference, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The cancellation was expected to succeed.");
        Assert.Equal(1, clock.UtcNowReads);
    }

    private Task<Result<Cancellation>> CancelAt(DateTimeOffset utcNow, StoredPolicyRepository repository, PolicyReference reference, DateOnly? on) =>
        new PolicyService(repository, _references, new FakeTimeProvider(utcNow)).Cancel(reference, on, TestContext.Current.CancellationToken);

    private static Cancellation AssertCancelledAndSaved(Result<Cancellation> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.True(result.IsSuccess, "The cancellation was expected to succeed.");
        Assert.Same(result.Value, policy.Cancellation);
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        return result.Value;
    }

    private RuleViolation AssertRejectedAndNotSaved(Result<Cancellation> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("CANCEL-3", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.Null(policy.Cancellation);
        Assert.Equal(0, repository.SaveChangesCalls);
        Assert.Equal(0, _references.RefundCalls);
        return violation;
    }

    private static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
