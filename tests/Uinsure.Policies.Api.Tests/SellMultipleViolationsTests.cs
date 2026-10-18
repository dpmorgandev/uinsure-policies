using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Every 422 rule is evaluated, none returns early: violations[] lists every broken rule in rule-ID order
// (SELL-2, SELL-4, SELL-5, SELL-7), and the top-level ruleId, type and title are the first one's.
public sealed class SellMultipleViolationsTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellMultipleViolationsTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // The contract's own 422 example: four policyholders, the second born 2011-01-01, start 2026-11-01.
    [Fact]
    [Trait("Rule", "SELL-4")]
    [Trait("Rule", "SELL-5")]
    public async Task Four_policyholders_with_one_under_16_reports_SELL_4_then_SELL_5()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("MULTI-SELL4-SELL5")
            .WithStartDate(new DateOnly(2026, 11, 1))
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Ada", "Lovelace"),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(new DateOnly(2011, 1, 1)),
                new PolicyholderBuilder().WithName("Mary", "Somerville"),
                new PolicyholderBuilder().WithName("Alan", "Turing"))
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.RuleProblem(response, HttpStatusCode.UnprocessableEntity, "SELL-4", "Wrong number of policyholders");
        Assert.Equal("2 rules failed.", problem.GetProperty("detail").GetString());
        var violations = problem.GetProperty("violations").EnumerateArray().ToList();
        Assert.Equal(["SELL-4", "SELL-5"], violations.Select(violation => violation.GetProperty("ruleId").GetString()));
        Assert.All(violations, violation => Assert.False(string.IsNullOrWhiteSpace(violation.GetProperty("detail").GetString())));
        await client.AssertNotStored("MULTI-SELL4-SELL5");
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    [Trait("Rule", "SELL-4")]
    [Trait("Rule", "SELL-5")]
    [Trait("Rule", "SELL-7")]
    public async Task Sale_breaking_SELL_2_SELL_4_SELL_5_and_SELL_7_reports_all_four_in_rule_order()
    {
        using var client = _factory.CreateClient();
        var start = TestClock.Today.AddDays(61);
        var body = new SellPolicyRequestBuilder("MULTI-ALL-FOUR")
            .WithStartDate(start)
            .WithAmount(0.00m)
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Ada", "Lovelace"),
                new PolicyholderBuilder().WithName("Charles", "Babbage").WithDateOfBirth(start.AddYears(-16).AddDays(1)),
                new PolicyholderBuilder().WithName("Mary", "Somerville"),
                new PolicyholderBuilder().WithName("Alan", "Turing"))
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.RuleProblem(response, HttpStatusCode.UnprocessableEntity, "SELL-2", "Start date outside the sale window");
        Assert.Equal("4 rules failed.", problem.GetProperty("detail").GetString());
        var violations = problem.GetProperty("violations").EnumerateArray().ToList();
        Assert.Equal(["SELL-2", "SELL-4", "SELL-5", "SELL-7"], violations.Select(violation => violation.GetProperty("ruleId").GetString()));
        Assert.All(violations, violation => Assert.False(string.IsNullOrWhiteSpace(violation.GetProperty("detail").GetString())));
        await client.AssertNotStored("MULTI-ALL-FOUR");
    }

    [Fact]
    [Trait("Rule", "SELL-2")]
    [Trait("Rule", "SELL-7")]
    public async Task Backdated_sale_with_a_zero_amount_reports_SELL_2_then_SELL_7()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("MULTI-SELL2-SELL7")
            .WithStartDate(TestClock.Today.AddDays(-1))
            .WithAmount(0.00m)
            .Build();

        using var response = await client.PostPolicy(body);

        var problem = await ProblemAssert.RuleProblem(response, HttpStatusCode.UnprocessableEntity, "SELL-2", "Start date outside the sale window");
        Assert.Equal("2 rules failed.", problem.GetProperty("detail").GetString());
        Assert.Equal(
            ["SELL-2", "SELL-7"],
            problem.GetProperty("violations").EnumerateArray().Select(violation => violation.GetProperty("ruleId").GetString()));
        await client.AssertNotStored("MULTI-SELL2-SELL7");
    }
}
