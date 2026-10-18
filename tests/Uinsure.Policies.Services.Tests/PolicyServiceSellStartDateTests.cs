using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-2 measured from today in Europe/London, which PolicyService.Sell reads once from the clock.
// At 23:30 UTC on 30 June 2027 (BST) the UK date is already 1 July, so the window is 2027-07-01 to
// 2027-08-30. At 23:30 UTC on 15 January 2027 (GMT) the UK date is still 15 January, so the window is
// 2027-01-15 to 2027-03-16. Each side of every boundary is its own test.
public sealed class PolicyServiceSellStartDateTests
{
    private static readonly DateTimeOffset BstLateEvening = TestClock.Utc(2027, 6, 30, 23, 30);
    private static readonly DateTimeOffset GmtLateEvening = TestClock.Utc(2027, 1, 15, 23, 30);

    private readonly RecordingPolicyRepository _repository = new();

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted()
    {
        var result = await SellAt(BstLateEvening, new SellPolicyBuilder("SELL2-SVC-BST-0").WithStartDate(new DateOnly(2027, 7, 1)).Build());

        AssertSoldAndSaved(result, new DateOnly(2027, 7, 1));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past()
    {
        var result = await SellAt(BstLateEvening, new SellPolicyBuilder("SELL2-SVC-BST-UTC").WithStartDate(new DateOnly(2027, 6, 30)).Build());

        var violation = AssertRejectedAndNotStored(result);
        Assert.Equal(new DateOnly(2027, 7, 1), Extension(violation, "earliest"));
        Assert.Equal(new DateOnly(2027, 8, 30), Extension(violation, "latest"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_UK_today_plus_60_at_23_30_UTC_in_BST_is_accepted()
    {
        var result = await SellAt(BstLateEvening, new SellPolicyBuilder("SELL2-SVC-BST-60").WithStartDate(new DateOnly(2027, 8, 30)).Build());

        AssertSoldAndSaved(result, new DateOnly(2027, 8, 30));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_UK_today_plus_61_at_23_30_UTC_in_BST_is_rejected()
    {
        var result = await SellAt(BstLateEvening, new SellPolicyBuilder("SELL2-SVC-BST-61").WithStartDate(new DateOnly(2027, 8, 31)).Build());

        var violation = AssertRejectedAndNotStored(result);
        Assert.Equal(new DateOnly(2027, 7, 1), Extension(violation, "earliest"));
        Assert.Equal(new DateOnly(2027, 8, 30), Extension(violation, "latest"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UTC_date_at_23_30_UTC_in_GMT_is_accepted()
    {
        var result = await SellAt(GmtLateEvening, new SellPolicyBuilder("SELL2-SVC-GMT-0").WithStartDate(new DateOnly(2027, 1, 15)).Build());

        AssertSoldAndSaved(result, new DateOnly(2027, 1, 15));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_the_day_before_the_UK_date_at_23_30_UTC_in_GMT_is_rejected_as_past()
    {
        var result = await SellAt(GmtLateEvening, new SellPolicyBuilder("SELL2-SVC-GMT-YDAY").WithStartDate(new DateOnly(2027, 1, 14)).Build());

        var violation = AssertRejectedAndNotStored(result);
        Assert.Equal(new DateOnly(2027, 1, 15), Extension(violation, "earliest"));
        Assert.Equal(new DateOnly(2027, 3, 16), Extension(violation, "latest"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_UK_today_plus_60_at_23_30_UTC_in_GMT_is_accepted()
    {
        var result = await SellAt(GmtLateEvening, new SellPolicyBuilder("SELL2-SVC-GMT-60").WithStartDate(new DateOnly(2027, 3, 16)).Build());

        AssertSoldAndSaved(result, new DateOnly(2027, 3, 16));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_UK_today_plus_61_at_23_30_UTC_in_GMT_is_rejected()
    {
        var result = await SellAt(GmtLateEvening, new SellPolicyBuilder("SELL2-SVC-GMT-61").WithStartDate(new DateOnly(2027, 3, 17)).Build());

        var violation = AssertRejectedAndNotStored(result);
        Assert.Equal(new DateOnly(2027, 1, 15), Extension(violation, "earliest"));
        Assert.Equal(new DateOnly(2027, 3, 16), Extension(violation, "latest"));
    }

    // Today is computed once per command, so the window and the sale agree on one date.
    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Sell_reads_the_clock_once()
    {
        var clock = new CountingTimeProvider(TestClock.Now);
        var service = new PolicyService(_repository, new ReferenceGenerator(), clock);

        var result = await service.Sell(new SellPolicyBuilder("SELL2-SVC-ONCE").Build(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, clock.UtcNowReads);
    }

    private Task<Result<PolicyDetails>> SellAt(DateTimeOffset utcNow, SellPolicy command) =>
        new PolicyService(_repository, new ReferenceGenerator(), new FakeTimeProvider(utcNow)).Sell(command, TestContext.Current.CancellationToken);

    private void AssertSoldAndSaved(Result<PolicyDetails> result, DateOnly expectedStart)
    {
        Assert.True(result.IsSuccess, "The sale was expected to succeed.");
        var saved = Assert.Single(_repository.Saved);
        Assert.Same(saved, result.Value.Policy);
        Assert.Equal(expectedStart, Assert.Single(saved.Terms).Start);
        Assert.Equal(1, _repository.SaveChangesCalls);
    }

    private RuleViolation AssertRejectedAndNotStored(Result<PolicyDetails> result)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("SELL-2", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.Equal(0, _repository.AddCalls);
        Assert.Equal(0, _repository.SaveChangesCalls);
        return violation;
    }

    private static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
