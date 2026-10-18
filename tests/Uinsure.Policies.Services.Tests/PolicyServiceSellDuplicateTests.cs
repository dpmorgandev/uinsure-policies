using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-1: a duplicate reference is a 409 conflict returned alone. The check runs before Policy.Sell, so a
// duplicate whose body also breaks 422 rules still gets only SELL-1, and nothing new is added or saved.
public sealed class PolicyServiceSellDuplicateTests
{
    private readonly RecordingPolicyRepository _repository = new();
    private readonly PolicyService _service;

    public PolicyServiceSellDuplicateTests()
    {
        _service = new PolicyService(_repository, new FakeTimeProvider(TestClock.Now));
    }

    // The second sale starts on today + 61 (breaks SELL-2) for an amount of 0 (breaks SELL-7).
    [Fact]
    [Trait("Rule", "SELL-1")]
    public async Task Duplicate_reference_with_a_body_that_breaks_SELL_2_and_SELL_7_fails_with_SELL_1_alone_and_nothing_is_saved()
    {
        var original = await Sell(new SellPolicyBuilder("SELL1-SVC-DUP").Build());
        Assert.True(original.IsSuccess, "The original sale was expected to succeed.");
        var addCallsBefore = _repository.AddCalls;
        var saveChangesCallsBefore = _repository.SaveChangesCalls;

        var duplicate = await Sell(new SellPolicyBuilder("SELL1-SVC-DUP")
            .WithStartDate(TestClock.Today.AddDays(61))
            .WithAmount(0.00m)
            .Build());

        Assert.False(duplicate.IsSuccess);
        var violation = Assert.Single(duplicate.Violations);
        Assert.Equal("SELL-1", violation.RuleId);
        Assert.Equal(ViolationKind.Conflict, violation.Kind);
        Assert.Equal("A policy with reference 'SELL1-SVC-DUP' already exists.", violation.Message);
        Assert.NotNull(violation.Extensions);
        Assert.Equal("SELL1-SVC-DUP", violation.Extensions["reference"]);
        Assert.Equal(addCallsBefore, _repository.AddCalls);
        Assert.Equal(saveChangesCallsBefore, _repository.SaveChangesCalls);
        Assert.Same(original.Value.Policy, Assert.Single(_repository.Saved));
    }

    private Task<Result<PolicyDetails>> Sell(SellPolicy command) =>
        _service.Sell(command, TestContext.Current.CancellationToken);
}
