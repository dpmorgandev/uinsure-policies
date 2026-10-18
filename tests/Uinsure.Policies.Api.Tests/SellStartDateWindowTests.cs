using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-2: the start date is between UK today and today + 60, inclusive; no backdating; otherwise 422.
// TestClock.Today is 2026-10-17, so the window is 2026-10-17 to 2026-12-16.
public sealed class SellStartDateWindowTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Start date outside the sale window";

    private readonly PoliciesApiFactory _factory;

    public SellStartDateWindowTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_today_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-AT-0").WithStartDate(TestClock.Today).Build();

        var policy = await client.SellAndReadBack(body, "SELL2-AT-0");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL2-AT-0", new DateOnly(2027, 10, 16));
        Assert.Equal(TestClock.Today, policy.GetDate("startDate"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_today_plus_60_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-AT-60").WithStartDate(TestClock.Today.AddDays(60)).Build();

        var policy = await client.SellAndReadBack(body, "SELL2-AT-60");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL2-AT-60", new DateOnly(2027, 12, 15));
        Assert.Equal(TestClock.Today.AddDays(60), policy.GetDate("startDate"));
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_today_plus_61_is_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-AT-61").WithStartDate(TestClock.Today.AddDays(61)).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-2", Title);
        Assert.Equal(TestClock.Today, problem.GetDate("earliest"));
        Assert.Equal(TestClock.Today.AddDays(60), problem.GetDate("latest"));
        await client.AssertNotStored("SELL2-AT-61");
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_yesterday_is_rejected_as_backdated()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-AT-MINUS-1").WithStartDate(TestClock.Today.AddDays(-1)).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-2", Title);
        Assert.Equal(TestClock.Today, problem.GetDate("earliest"));
        Assert.Equal(TestClock.Today.AddDays(60), problem.GetDate("latest"));
        await client.AssertNotStored("SELL2-AT-MINUS-1");
    }

    // 2027-06-30T23:30Z is 00:30 BST on 2027-07-01, so UK today is 2027-07-01 and the UTC date is past.
    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 30, 23, 30, 0, TimeSpan.Zero));
        var ukToday = new DateOnly(2027, 7, 1);
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-BST-UTC").WithStartDate(new DateOnly(2027, 6, 30)).Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-2", Title);
        Assert.Equal(ukToday, problem.GetDate("earliest"));
        Assert.Equal(ukToday.AddDays(60), problem.GetDate("latest"));
        await client.AssertNotStored("SELL2-BST-UTC");
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 6, 30, 23, 30, 0, TimeSpan.Zero));
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-BST-UK").WithStartDate(new DateOnly(2027, 7, 1)).Build();

        var policy = await client.SellAndReadBack(body, "SELL2-BST-UK");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL2-BST-UK", new DateOnly(2028, 6, 30));
        Assert.Equal(new DateOnly(2027, 7, 1), policy.GetDate("startDate"));
    }

    // GMT control: at 2027-01-15T23:30Z the UK and UTC dates agree, so that date is today and is sold.
    [Fact]
    [Trait("Rule", "SELL-2")]
    public async Task Start_date_of_the_UTC_date_at_23_30_UTC_in_GMT_is_accepted_as_today()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 15, 23, 30, 0, TimeSpan.Zero));
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL2-GMT-UTC").WithStartDate(new DateOnly(2027, 1, 15)).Build();

        var policy = await client.SellAndReadBack(body, "SELL2-GMT-UTC");

        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL2-GMT-UTC", new DateOnly(2028, 1, 14));
        Assert.Equal(new DateOnly(2027, 1, 15), policy.GetDate("startDate"));
    }
}
