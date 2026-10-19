using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// RENEW-1/2 at the layer that knows the clock: PolicyService.Renew reads today once, in Europe/London, and hands
// it to policy.Renew, whose window is the latest term's end - 30 days to its end, inclusive. A renewal inside the
// window appends the term and saves; one outside it is 422 and nothing is saved. The domain's own boundaries are
// proven in Domain.Tests.
//   BST: a term starting 2026-08-01 ends 2027-07-31, so its window is 2027-07-01 to 2027-07-31. At 23:30 UTC on
//   30 June 2027 the UK date is already 1 July (opens); at 23:30 UTC on 31 July it is already 1 August (closed).
//   GMT control: a term starting 2026-02-16 ends 2027-02-15, so its window is 2027-01-16 to 2027-02-15. At 23:30
//   UTC on 15 January 2027 the UK date is still 15 January (not yet open); on 15 February it is still 15 February.
public sealed class PolicyServiceRenewWindowTests
{
    private static readonly DateOnly BstTermStart = new(2026, 8, 1);
    private static readonly DateOnly GmtTermStart = new(2026, 2, 16);

    private readonly SequentialReferenceGenerator _references = new();

    // The UTC date, 2027-06-30, is the day before the window opens; the UK date is its first day.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_at_23_30_UTC_in_BST_the_evening_before_the_window_opens_is_accepted_on_the_UK_date()
    {
        var policy = new StoredPolicyBuilder("R1-SVC-BST-OPEN").WithTermsStarting(BstTermStart).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(TestClock.Utc(2027, 6, 30, 23, 30), repository, policy.Reference);

        var term = AssertRenewedAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 8, 1), term.Start);
        Assert.Equal(new DateOnly(2028, 7, 31), term.End);
    }

    // The UK date is the UTC date, 2027-01-15, the day before the window opens.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    public async Task Renewal_at_23_30_UTC_in_GMT_the_evening_before_the_window_opens_is_rejected()
    {
        var policy = new StoredPolicyBuilder("R1-SVC-GMT-OPEN").WithTermsStarting(GmtTermStart).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(TestClock.Utc(2027, 1, 15, 23, 30), repository, policy.Reference);

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 1, 16), Extension(violation, "windowStart"));
        Assert.Equal(new DateOnly(2027, 2, 15), Extension(violation, "windowEnd"));
    }

    // The UTC date, 2027-07-31, is the last day of the window; the UK date is the day after it.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_at_23_30_UTC_in_BST_on_the_end_date_is_rejected_as_after_it()
    {
        var policy = new StoredPolicyBuilder("R2-SVC-BST-END").WithTermsStarting(BstTermStart).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(TestClock.Utc(2027, 7, 31, 23, 30), repository, policy.Reference);

        var violation = AssertRejectedAndNotSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 7, 1), Extension(violation, "windowStart"));
        Assert.Equal(new DateOnly(2027, 7, 31), Extension(violation, "windowEnd"));
    }

    // The UK date is the UTC date, 2027-02-15, the end date and the last day of the window.
    [Fact]
    [Trait("Rule", "RENEW-2")]
    public async Task Renewal_at_23_30_UTC_in_GMT_on_the_end_date_is_accepted()
    {
        var policy = new StoredPolicyBuilder("R2-SVC-GMT-END").WithTermsStarting(GmtTermStart).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(TestClock.Utc(2027, 2, 15, 23, 30), repository, policy.Reference);

        var term = AssertRenewedAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 2, 16), term.Start);
        Assert.Equal(new DateOnly(2028, 2, 15), term.End);
    }

    // Today is computed once per command, so the window check and the defaulted payment method agree on one date.
    // 2027-10-20 is inside the default stored term's window, 2027-10-08 to 2027-11-07.
    [Fact]
    [Trait("Rule", "RENEW-1")]
    [Trait("Rule", "RENEW-2")]
    public async Task Renew_reads_the_clock_once()
    {
        var policy = new StoredPolicyBuilder("R12-SVC-ONCE").Build();
        var clock = new CountingTimeProvider(TestClock.Utc(2027, 10, 20, 12, 0));
        var service = new PolicyService(new StoredPolicyRepository(policy), _references, clock);

        var result = await service.Renew(policy.Reference, 563.19m, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The renewal was expected to succeed.");
        Assert.Equal(1, clock.UtcNowReads);
    }

    private Task<Result<PolicyDetails>> RenewAt(DateTimeOffset utcNow, StoredPolicyRepository repository, PolicyReference reference) =>
        new PolicyService(repository, _references, new FakeTimeProvider(utcNow))
            .Renew(reference, 563.19m, null, TestContext.Current.CancellationToken);

    // The renewal appends term 2, the policy's latest term, and returns the renewed policy as of the UK date. Every
    // accepted renewal here falls on a day of term 1's window, so term 1 is current and the policy is Active.
    private Term AssertRenewedAndSaved(Result<PolicyDetails> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.True(result.IsSuccess, "The renewal was expected to succeed.");
        Assert.Equal(2, policy.Terms.Count);
        Assert.Equal(new Payment("PAY-SVC-1", PaymentMethod.DirectDebit, 563.19m), policy.LatestTerm.Payment);
        Assert.Same(policy, result.Value.Policy);
        Assert.Same(policy.Terms[0], result.Value.CurrentTerm);
        Assert.Equal(PolicyStatus.Active, result.Value.Status);
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        return policy.LatestTerm;
    }

    private RuleViolation AssertRejectedAndNotSaved(Result<PolicyDetails> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("RENEW-1/2", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.Single(policy.Terms);
        Assert.Equal(0, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, _references.PaymentCalls);
        return violation;
    }

    private static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
