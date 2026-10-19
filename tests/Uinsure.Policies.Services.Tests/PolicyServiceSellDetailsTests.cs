using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// The details a sale answers with come from the domain, on the UK today the sale was made on: the status is
// Policy.StatusOn(today) and the current term Policy.CurrentTerm(today), never a placeholder. Traited GET-1 in
// F2.
public sealed class PolicyServiceSellDetailsTests
{
    private readonly RecordingPolicyRepository _repository = new();
    private readonly PolicyService _service;

    public PolicyServiceSellDetailsTests()
    {
        _service = new PolicyService(_repository, new ReferenceGenerator(), new FakeTimeProvider(TestClock.Now));
    }

    // The default sale starts on 2026-11-08, 22 days after today (2026-10-17).
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Sale_starting_after_today_is_returned_as_Pending_with_the_sold_term_as_current()
    {
        var result = await _service.Sell(new SellPolicyBuilder("F1B-SVC-SELL-PEND").Build(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The sale was expected to succeed.");
        Assert.Equal(PolicyStatus.Pending, result.Value.Status);
        var sold = Assert.Single(result.Value.Policy.Terms);
        Assert.Same(sold, result.Value.CurrentTerm);
        Assert.Equal(new DateOnly(2026, 11, 8), result.Value.CurrentTerm.Start);
    }
}
