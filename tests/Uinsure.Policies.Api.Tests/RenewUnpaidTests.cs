using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-3b: renewing a policy that does not auto-renew creates the new term unpaid: its paymentReference is null
// and no payment is added (collecting it is out of scope). A request that sends a paymentMethod anyway is 422
// /problems/renew-3b, because the renewal is unpaid; that holds for Cheque too, and COULD-3 (no Cheque for an
// auto-renewal) does not join it, since the policy does not auto-renew. A Cheque policy without auto-renew is
// renewed unpaid like any other. Each policy is sold with auto-renew off, the clock moved into its window, and
// read back whole:
//   start 2026-11-16 ends 2027-11-15; term 2 2027-11-16 to 2028-11-15 (the Distinctive sale);
//   start 2026-11-02 ends 2027-11-01 (rejected: one term);
//   start 2026-11-06 ends 2027-11-05 (rejected: one term);
//   start 2026-12-14 ends 2027-12-13; term 2 2027-12-14 to 2028-12-13.
// Cancelling a policy with an unpaid renewed term is CancelRenewedPolicyTests' UNPAID-TERM row.
public sealed class RenewUnpaidTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Payment method sent for an unpaid renewal";

    private readonly PoliciesApiFactory _factory;

    public RenewUnpaidTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public async Task Renewal_without_auto_renew_and_without_a_payment_method_creates_an_unpaid_term()
    {
        const string reference = "RENEW3B-UNPAID";
        var sold = SellPolicyRequestBuilder.Distinctive(reference).Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 30));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(436.51m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 11, 16), new DateOnly(2027, 11, 15), "412.34", "Card"),
                new ExpectedPolicyTerm(new DateOnly(2027, 11, 16), new DateOnly(2028, 11, 15), "436.51", null),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public async Task Renewal_without_auto_renew_with_a_payment_method_is_rejected_with_422()
    {
        const string reference = "RENEW3B-WITH-METHOD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 2)).WithAmount(349.95m).WithAutoRenew(false).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 20));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(362.40m).WithPaymentMethod("Card").Build());

        await ProblemAssert.SingleRuleViolation(response, "RENEW-3b", Title, RenewalActions.RenewalsPath(reference));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 1), "Active");
    }

    // Finding A1/R2. The contract's RENEW-3b row has no extensions ('—'): the problem carries only the members
    // every 422 carries, and its one violation only ruleId and detail. Start 2026-11-20 ends 2027-11-19.
    [Fact]
    [Trait("Rule", "RENEW-3b")]
    public async Task Renewal_without_auto_renew_with_a_payment_method_is_rejected_with_no_extension_members()
    {
        const string reference = "RENEW3B-NO-EXT";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 20)).WithAmount(357.83m).WithAutoRenew(false).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 3));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(371.46m).WithPaymentMethod("DirectDebit").Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "RENEW-3b", Title, RenewalActions.RenewalsPath(reference));
        PolicyResponseAssert.HasMembers(
            problem, ["type", "title", "status", "detail", "instance", "ruleId", "traceId", "violations"], "RENEW-3b problem");
        PolicyResponseAssert.HasMembers(Assert.Single(problem.Member("violations").EnumerateArray()), ["ruleId", "detail"], "RENEW-3b violation");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 19), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "COULD-3")]
    public async Task Renewal_without_auto_renew_with_Cheque_is_rejected_for_RENEW_3b_alone()
    {
        const string reference = "RENEW3B-CHEQUE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 6)).WithAmount(366.66m).WithAutoRenew(false).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 26));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(380.25m).WithPaymentMethod("Cheque").Build());

        await ProblemAssert.SingleRuleViolation(response, "RENEW-3b", Title, RenewalActions.RenewalsPath(reference));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 5), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "COULD-3")]
    public async Task Renewal_without_auto_renew_of_a_Cheque_policy_creates_an_unpaid_term()
    {
        const string reference = "RENEW3B-CHQ-UNPAID";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 14)).WithAmount(452.61m).WithAutoRenew(false).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 12, 1));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(468.12m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 12, 14), new DateOnly(2027, 12, 13), "452.61", "Cheque"),
                new ExpectedPolicyTerm(new DateOnly(2027, 12, 14), new DateOnly(2028, 12, 13), "468.12", null),
            ]),
            [sale.PaymentReference()]);
    }
}
