using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-3a: renewing an auto-renewing policy creates a payment for the new term's amount, by the request's
// paymentMethod, or, when none is sent, by the current term's payment method. The payment has a generated
// reference of its own, which the new term links to; term 1's payment is unchanged. Each policy is sold (E1)
// with auto-renew on, the clock is moved into its renewal window, and the renewal (E6) is read back whole (E2):
//   start 2026-11-19 ends 2027-11-18; term 2 2027-11-19 to 2028-11-18;
//   start 2026-12-10 ends 2027-12-09; term 2 2027-12-10 to 2028-12-09;
//   start 2026-11-25 ends 2027-11-24; term 2 2027-11-25 to 2028-11-24;
//   start 2026-10-23 ends 2027-10-22; term 2 2027-10-23 to 2028-10-22.
// The default taken from a renewed term rather than the first is RenewTermTests' third-term row.
public sealed class RenewPaymentTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public RenewPaymentTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_without_a_payment_method_is_paid_by_the_current_terms_Direct_Debit()
    {
        const string reference = "RENEW3A-DEFAULT-DD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 19)).WithAmount(538.46m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 5));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(557.09m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 11, 19), new DateOnly(2027, 11, 18), "538.46", "DirectDebit"),
                new ExpectedPolicyTerm(new DateOnly(2027, 11, 19), new DateOnly(2028, 11, 18), "557.09", "DirectDebit"),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_without_a_payment_method_is_paid_by_the_current_terms_Card()
    {
        const string reference = "RENEW3A-DEFAULT-CARD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 10)).WithAmount(611.72m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 25));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(634.05m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 12, 10), new DateOnly(2027, 12, 9), "611.72", "Card"),
                new ExpectedPolicyTerm(new DateOnly(2027, 12, 10), new DateOnly(2028, 12, 9), "634.05", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_with_Card_on_a_Direct_Debit_policy_is_paid_by_Card()
    {
        const string reference = "RENEW3A-REQUEST-CARD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 25)).WithAmount(492.38m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 15));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(515.60m).WithPaymentMethod("Card").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 11, 25), new DateOnly(2027, 11, 24), "492.38", "DirectDebit"),
                new ExpectedPolicyTerm(new DateOnly(2027, 11, 25), new DateOnly(2028, 11, 24), "515.60", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    // The amount is sent as 390, a whole number of pounds, and written back as 390.00 in the term and its payment.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Auto_renewal_with_Direct_Debit_on_a_Card_policy_is_paid_by_Direct_Debit()
    {
        const string reference = "RENEW3A-REQUEST-DD";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 10, 23)).WithAmount(377.14m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 1));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(390m).WithPaymentMethod("DirectDebit").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 10, 23), new DateOnly(2027, 10, 22), "377.14", "Card"),
                new ExpectedPolicyTerm(new DateOnly(2027, 10, 23), new DateOnly(2028, 10, 22), "390.00", "DirectDebit"),
            ]),
            [sale.PaymentReference()]);
    }
}
