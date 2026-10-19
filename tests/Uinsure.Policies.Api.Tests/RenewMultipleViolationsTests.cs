using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// E6's 422 rules are all evaluated and none returns early: every one broken is listed in violations[], in
// rule-ID order (RENEW-1/2, RENEW-3b, RENEW-5, COULD-3), the top-level ruleId, type and title are the first's,
// and the detail counts the rules. The policy reads back as sold. Policies sold today renew far outside their
// window (the rows Postman runs in real time); the others move the clock:
//   start 2026-10-17 ends 2027-10-16, renewed today;
//   start 2026-10-25 ends 2027-10-24, renewed today;
//   start 2026-11-23 ends 2027-11-22, renewed on 2027-11-02, inside the window;
//   start 2026-11-01 ends 2027-10-31, renewed on 2027-11-05, after the end (Expired).
public sealed class RenewMultipleViolationsTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public RenewMultipleViolationsTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-1")]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_today_without_auto_renew_with_a_method_and_amount_0_reports_RENEW_1_2_RENEW_3b_and_RENEW_5()
    {
        const string reference = "RENEW-MULTI-3B";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(TestClock.Today).WithAmount(341.58m).WithAutoRenew(false).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(0.00m).WithPaymentMethod("Card").Build());

        await ProblemAssert.MultipleRuleViolations(
            response, "Outside the renewal window", RenewalActions.RenewalsPath(reference), "RENEW-1/2", "RENEW-3b", "RENEW-5");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 16), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-1")]
    [Trait("Rule", "RENEW-5")]
    [Trait("Rule", "COULD-3")]
    public async Task Auto_renewal_today_by_Cheque_for_a_negative_amount_reports_RENEW_1_2_RENEW_5_and_COULD_3()
    {
        const string reference = "RENEW-MULTI-COULD3";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 10, 25)).WithAmount(364.52m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(-1.00m).WithPaymentMethod("Cheque").Build());

        await ProblemAssert.MultipleRuleViolations(
            response, "Outside the renewal window", RenewalActions.RenewalsPath(reference), "RENEW-1/2", "RENEW-5", "COULD-3");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 24), "Pending");
    }

    // Inside the window the first rule broken is RENEW-5, so it names the problem.
    [Fact]
    [Trait("Rule", "RENEW-5")]
    [Trait("Rule", "COULD-3")]
    public async Task Auto_renewal_in_the_window_for_0_with_a_defaulted_Cheque_reports_RENEW_5_and_COULD_3()
    {
        const string reference = "RENEW-MULTI-WINDOW";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 23)).WithAmount(476.39m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 2));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(0.00m).Build());

        await ProblemAssert.MultipleRuleViolations(
            response, "Renewal amount must be greater than zero", RenewalActions.RenewalsPath(reference), "RENEW-5", "COULD-3");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 22), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-2")]
    [Trait("Rule", "RENEW-3b")]
    public async Task Renewal_after_the_end_without_auto_renew_with_a_method_reports_RENEW_1_2_and_RENEW_3b()
    {
        const string reference = "RENEW-MULTI-EXPIRED";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 1)).WithAmount(398.73m).WithAutoRenew(false).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 5));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(410.00m).WithPaymentMethod("DirectDebit").Build());

        await ProblemAssert.MultipleRuleViolations(
            response, "Outside the renewal window", RenewalActions.RenewalsPath(reference), "RENEW-1/2", "RENEW-3b");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 10, 31), "Expired");
    }
}
