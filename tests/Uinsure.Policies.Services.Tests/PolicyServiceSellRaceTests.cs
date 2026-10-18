using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-1 on the race path. Two sales of one reference can both pass the GetByReference check; the second to
// save then hits the unique index, and the repository raises PersistenceConflictException(DuplicateReference).
// PolicyService answers that exactly as the check does: SELL-1, a conflict, alone. Only DuplicateReference is
// converted; a Concurrency conflict is not a duplicate and goes on to the API's /problems/concurrency 409.
public sealed class PolicyServiceSellRaceTests
{
    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Sale_that_passes_the_duplicate_check_and_then_hits_the_unique_index_on_save_fails_with_SELL_1_alone()
    {
        var repository = new ConflictOnSavePolicyRepository(ConflictReason.DuplicateReference);
        var service = new PolicyService(repository, new FakeTimeProvider(TestClock.Now));

        var result = await service.Sell(new SellPolicyBuilder("SELL1-SVC-RACE").Build(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("SELL-1", violation.RuleId);
        Assert.Equal(ViolationKind.Conflict, violation.Kind);
        Assert.Equal("A policy with reference 'SELL1-SVC-RACE' already exists.", violation.Message);
        Assert.NotNull(violation.Extensions);
        Assert.Equal("SELL1-SVC-RACE", violation.Extensions["reference"]);
        Assert.Equal(1, repository.GetByReferenceCalls);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task Concurrency_conflict_on_saving_a_sale_is_not_turned_into_SELL_1()
    {
        var repository = new ConflictOnSavePolicyRepository(ConflictReason.Concurrency);
        var service = new PolicyService(repository, new FakeTimeProvider(TestClock.Now));

        var conflict = await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            service.Sell(new SellPolicyBuilder("SELL1-SVC-STALE").Build(), TestContext.Current.CancellationToken));

        Assert.Equal(ConflictReason.Concurrency, conflict.Reason);
        Assert.Equal(1, repository.SaveChangesCalls);
    }
}
