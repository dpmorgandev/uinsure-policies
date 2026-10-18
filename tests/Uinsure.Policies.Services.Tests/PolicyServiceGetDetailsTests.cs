using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// PolicyService.Get answers with the domain's view on UK today: the status is Policy.StatusOn(today) and the
// current term Policy.CurrentTerm(today), the term covering today, else the latest. The domain's boundaries are
// proven in Domain.Tests; these prove the service asks the domain, with today in Europe/London. At 23:30 UTC on
// 30 June 2027 (BST) the UK date is already 1 July. Traited GET-1 in F2.
public sealed class PolicyServiceGetDetailsTests
{
    private static readonly DateTimeOffset BstLateEvening = TestClock.Utc(2027, 6, 30, 23, 30);

    private readonly RecordingPolicyRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(TestClock.Now);
    private readonly PolicyService _service;

    public PolicyServiceGetDetailsTests()
    {
        _service = new PolicyService(_repository, new ReferenceGenerator(), _clock);
    }

    // The one term runs from 2026-11-08 to 2027-11-07; today is 2026-10-17.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Get_before_the_start_date_returns_Pending()
    {
        var policy = await Store(new StoredPolicyBuilder("F1B-SVC-GET-PEND").Build());

        var details = await Found(policy.Reference);

        Assert.Equal(PolicyStatus.Pending, details.Status);
        Assert.Same(Assert.Single(policy.Terms), details.CurrentTerm);
    }

    // The one term runs from 2026-11-08 to 2027-11-07; today is moved on to 2027-11-08.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Get_the_day_after_the_end_date_returns_Expired()
    {
        var policy = await Store(new StoredPolicyBuilder("F1B-SVC-GET-EXP").Build());
        _clock.SetUtcNow(TestClock.Utc(2027, 11, 8, 12, 0));

        var details = await Found(policy.Reference);

        Assert.Equal(PolicyStatus.Expired, details.Status);
        Assert.Same(Assert.Single(policy.Terms), details.CurrentTerm);
    }

    // Terms 2025-10-01 to 2026-09-30 and 2026-10-01 to 2027-09-30; today (2026-10-17) is in the second.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Get_of_a_policy_with_two_terms_returns_the_term_covering_today_as_current()
    {
        var policy = await Store(new StoredPolicyBuilder("F1B-SVC-GET-TWO")
            .WithTermsStarting(new DateOnly(2025, 10, 1), new DateOnly(2026, 10, 1))
            .Build());

        var details = await Found(policy.Reference);

        Assert.Same(policy.Terms[1], details.CurrentTerm);
        Assert.Equal(new DateOnly(2026, 10, 1), details.CurrentTerm.Start);
        Assert.Equal(PolicyStatus.Active, details.Status);
    }

    // The one term runs from 2026-07-01 to 2027-06-30: the UTC date is its last day, the UK date the day after.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Get_at_23_30_UTC_in_BST_on_the_UTC_date_of_the_end_returns_Expired()
    {
        var policy = await Store(new StoredPolicyBuilder("F1B-SVC-GET-BSTEXP")
            .WithTermsStarting(new DateOnly(2026, 7, 1))
            .Build());
        _clock.SetUtcNow(BstLateEvening);

        var details = await Found(policy.Reference);

        Assert.Equal(PolicyStatus.Expired, details.Status);
    }

    // Terms 2026-07-01 to 2027-06-30 and 2027-07-01 to 2028-06-30: the UTC date is in the first, the UK date
    // in the second.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Get_at_23_30_UTC_in_BST_returns_the_term_starting_on_the_UK_date_as_current()
    {
        var policy = await Store(new StoredPolicyBuilder("F1B-SVC-GET-BSTCUR")
            .WithTermsStarting(new DateOnly(2026, 7, 1), new DateOnly(2027, 7, 1))
            .Build());
        _clock.SetUtcNow(BstLateEvening);

        var details = await Found(policy.Reference);

        Assert.Same(policy.Terms[1], details.CurrentTerm);
        Assert.Equal(new DateOnly(2027, 7, 1), details.CurrentTerm.Start);
        Assert.Equal(PolicyStatus.Active, details.Status);
    }

    private async Task<Policy> Store(Policy policy)
    {
        _repository.Add(policy);
        await _repository.SaveChanges(TestContext.Current.CancellationToken);
        return policy;
    }

    private async Task<PolicyDetails> Found(PolicyReference reference)
    {
        Result<PolicyDetails> result = await _service.Get(reference, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, "The stored policy was expected to be found.");
        Assert.Same(Assert.Single(_repository.Saved), result.Value.Policy);
        return result.Value;
    }
}
