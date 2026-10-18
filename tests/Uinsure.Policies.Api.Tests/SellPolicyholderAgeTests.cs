using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-5: every policyholder is 16 or older on the start date (a 16th birthday on the start date passes),
// else 422. The start date here is 2026-11-01, so a 16th birthday on it means born 2010-11-01.
public sealed class SellPolicyholderAgeTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Policyholder under 16";

    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    private readonly PoliciesApiFactory _factory;

    public SellPolicyholderAgeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-5")]
    public async Task Policyholder_whose_16th_birthday_is_the_start_date_is_accepted()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL5-16TH-ON-START")
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(Start.AddYears(-16)))
            .Build();

        var policy = await client.SellAndReadBack(body, "SELL5-16TH-ON-START");

        var holder = Assert.Single(policy.GetProperty("policyholders").EnumerateArray());
        Assert.Equal(Start.AddYears(-16), holder.GetDate("dateOfBirth"));
    }

    [Fact]
    [Trait("Rule", "SELL-5")]
    public async Task Policyholder_whose_16th_birthday_is_the_day_after_the_start_date_is_rejected()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL5-16TH-AFTER")
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithDateOfBirth(Start.AddYears(-16).AddDays(1)))
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-5", Title);
        Assert.Equal(1, problem.GetProperty("index").GetInt32());
        await client.AssertNotStored("SELL5-16TH-AFTER");
    }

    // Age is measured on the start date, not today: 15 today (2026-10-17), 16 by the start (2026-11-01).
    [Fact]
    [Trait("Rule", "SELL-5")]
    public async Task Policyholder_under_16_today_but_16_on_the_start_date_is_accepted()
    {
        using var client = _factory.CreateClient();
        var bornOn = new DateOnly(2010, 10, 25);
        var body = new SellPolicyRequestBuilder("SELL5-16-BY-START")
            .WithStartDate(Start)
            .WithPolicyholders(new PolicyholderBuilder().WithName("Ada", "Lovelace").WithDateOfBirth(bornOn))
            .Build();

        var policy = await client.SellAndReadBack(body, "SELL5-16-BY-START");

        // The start date matches the canned example, so the date of birth carries the proof.
        SoldPolicyAssert.ReadsBackAsSold(policy, body, "SELL5-16-BY-START", new DateOnly(2027, 10, 31));
        var holder = Assert.Single(policy.Member("policyholders").EnumerateArray());
        Assert.Equal("Ada", holder.Member("firstName").GetString());
        Assert.Equal("Lovelace", holder.Member("lastName").GetString());
        Assert.Equal(new DateOnly(2010, 10, 25), holder.GetDate("dateOfBirth"));
        Assert.Equal(new DateOnly(2026, 11, 1), policy.GetDate("startDate"));
    }

    // index is the policyholder's 1-based position, as in the contract's "Policyholder 2 (born …)" detail.
    [Fact]
    [Trait("Rule", "SELL-5")]
    public async Task Second_policyholder_under_16_on_the_start_date_is_rejected_and_identified_by_index_2()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL5-SECOND-UNDER")
            .WithStartDate(Start)
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Ada", "Lovelace"),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(new DateOnly(2011, 1, 1)))
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.SingleRuleViolation(response, "SELL-5", Title);
        Assert.Equal(2, problem.GetProperty("index").GetInt32());
        await client.AssertNotStored("SELL5-SECOND-UNDER");
    }
}
