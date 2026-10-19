using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// COULD-2a at the layer that knows the clock: PolicyService.RecordClaim reads today once, in Europe/London, and
// checks the claim date against it. A claim dated UK today is recorded and saved; one dated after it is 422 and
// nothing is saved. The domain's own boundaries are proven in Domain.Tests.
//   At 23:30 UTC on 30 June 2027 (BST) the UK date is already 1 July; at 23:30 UTC on 15 January 2027 (GMT) it
//   is still 15 January. Every claim date below is within a term's cover, so only COULD-2a can reject it.
//   The default stored term runs 2026-11-08 to 2027-11-07; a term starting 2027-07-01 runs to 2028-06-30.
public sealed class PolicyServiceClaimDateTests
{
    private const string Description = "Water through the kitchen ceiling";

    private static readonly DateTimeOffset BstLateEvening = TestClock.Utc(2027, 6, 30, 23, 30);
    private static readonly DateTimeOffset GmtLateEvening = TestClock.Utc(2027, 1, 15, 23, 30);

    // The UTC date (30 June) would make a 1 July claim a day in the future.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_the_UK_date_at_23_30_UTC_in_BST_is_recorded()
    {
        var policy = new StoredPolicyBuilder("C2A-SVC-BST-0").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RecordAt(BstLateEvening, repository, policy.Reference, new DateOnly(2027, 7, 1));

        AssertRecordedAndSaved(result, policy, repository, new DateOnly(2027, 7, 1));
    }

    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_the_day_after_the_UK_date_at_23_30_UTC_in_BST_is_rejected()
    {
        var policy = new StoredPolicyBuilder("C2A-SVC-BST-1").WithTermsStarting(new DateOnly(2027, 7, 1)).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RecordAt(BstLateEvening, repository, policy.Reference, new DateOnly(2027, 7, 2));

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 7, 1), Extension(violation, "today"));
    }

    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_the_UK_date_at_23_30_UTC_in_GMT_is_recorded()
    {
        var policy = new StoredPolicyBuilder("C2A-SVC-GMT-0").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RecordAt(GmtLateEvening, repository, policy.Reference, new DateOnly(2027, 1, 15));

        AssertRecordedAndSaved(result, policy, repository, new DateOnly(2027, 1, 15));
    }

    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task Claim_dated_the_day_after_the_UK_date_at_23_30_UTC_in_GMT_is_rejected()
    {
        var policy = new StoredPolicyBuilder("C2A-SVC-GMT-1").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RecordAt(GmtLateEvening, repository, policy.Reference, new DateOnly(2027, 1, 16));

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 1, 15), Extension(violation, "today"));
    }

    // Today is computed once per command.
    [Fact]
    [Trait("Rule", "COULD-2a")]
    public async Task RecordClaim_reads_the_clock_once()
    {
        var policy = new StoredPolicyBuilder("C2A-SVC-ONCE").WithTermsStarting(new DateOnly(2026, 10, 1)).Build();
        var clock = new CountingTimeProvider(TestClock.Now);
        var service = new PolicyService(new StoredPolicyRepository(policy), new SequentialReferenceGenerator(), clock);

        var result = await service.RecordClaim(policy.Reference, TestClock.Today, Description, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The claim was expected to be recorded.");
        Assert.Equal(1, clock.UtcNowReads);
    }

    private static Task<Result<RecordedClaim>> RecordAt(DateTimeOffset utcNow, StoredPolicyRepository repository, PolicyReference reference, DateOnly claimDate) =>
        new PolicyService(repository, new SequentialReferenceGenerator(), new FakeTimeProvider(utcNow))
            .RecordClaim(reference, claimDate, Description, TestContext.Current.CancellationToken);

    private static void AssertRecordedAndSaved(Result<RecordedClaim> result, Policy policy, StoredPolicyRepository repository, DateOnly expectedDate)
    {
        Assert.True(result.IsSuccess, "The claim was expected to be recorded.");
        Assert.Equal(new Claim(expectedDate, Description), result.Value.Claim);
        Assert.Equal([new Claim(expectedDate, Description)], policy.Claims);
        Assert.True(policy.HasClaims);
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
    }

    private static RuleViolation AssertRejectedAndNotSaved(Result<RecordedClaim> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("COULD-2a", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.Empty(policy.Claims);
        Assert.False(policy.HasClaims);
        Assert.Equal(0, repository.SaveChangesCalls);
        return violation;
    }

    private static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
