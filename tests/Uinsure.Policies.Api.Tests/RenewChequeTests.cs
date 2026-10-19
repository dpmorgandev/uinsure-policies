using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// COULD-3: an auto-renewing policy's renewal cannot be paid by Cheque, whether the Cheque is sent or defaulted
// from the current term's payment (RENEW-3a): 422 /problems/could-3 with paymentMethod Cheque, and for the
// defaulted Cheque a detail that asks for another method (Card or DirectDebit). Sending another method renews a
// Cheque policy. A policy that does not auto-renew is unpaid on renewal, so COULD-3 does not apply to it
// (RenewUnpaidTests). Each policy is sold with auto-renew on, the clock moved into its window, and read back
// whole:
//   start 2026-11-09 ends 2027-11-08 (rejected: one term);
//   start 2026-11-27 ends 2027-11-26 (rejected: one term);
//   start 2026-12-03 ends 2027-12-02; term 2 2027-12-03 to 2028-12-02.
public sealed class RenewChequeTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Cheque not allowed for auto-renewal";

    private readonly PoliciesApiFactory _factory;

    public RenewChequeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    public async Task Auto_renewal_with_an_explicit_Cheque_is_rejected_with_422()
    {
        const string reference = "COULD3-EXPLICIT";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 9)).WithAmount(459.63m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 27));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(472.30m).WithPaymentMethod("Cheque").Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-3", Title, RenewalActions.RenewalsPath(reference));
        Assert.Equal("Cheque", problem.Member("paymentMethod").GetString());
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 8), "Active");
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_defaulting_to_the_current_terms_Cheque_is_rejected_with_422_asking_for_another_method()
    {
        const string reference = "COULD3-DEFAULTED";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 27)).WithAmount(501.19m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 12));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(523.48m).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "COULD-3", Title, RenewalActions.RenewalsPath(reference));
        Assert.Equal("Cheque", problem.Member("paymentMethod").GetString());
        var detail = problem.Member("detail").GetString();
        Assert.Contains("Card", detail, StringComparison.Ordinal);
        Assert.Contains("DirectDebit", detail, StringComparison.Ordinal);
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 26), "Active");
    }

    [Fact]
    [Trait("Rule", "COULD-3")]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_of_a_Cheque_policy_by_Card_is_accepted()
    {
        const string reference = "COULD3-CHEQUE-CARD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 3)).WithAmount(478.92m).WithPaymentMethod("Cheque").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 18));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(495.37m).WithPaymentMethod("Card").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 12, 3), new DateOnly(2027, 12, 2), "478.92", "Cheque"),
                new ExpectedPolicyTerm(new DateOnly(2027, 12, 3), new DateOnly(2028, 12, 2), "495.37", "Card"),
            ]),
            [sale.PaymentReference()]);
    }
}
