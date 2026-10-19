using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-5: the client supplies the new term's amount, which must be greater than 0, else 422 /problems/renew-5
// with the amount sent as an extension, written with 2 decimal places. Each side of the boundary is its own test:
// 0.01 is accepted, 0.00 and a negative amount are rejected. The upper bound, 1,000,000.00, is a 400 shape rule
// (RenewShapeTests); 1,000,000.00 itself is accepted here. Each policy is sold (E1), the clock moved into its
// renewal window, and the policy read back whole (E2):
//   start 2026-10-31 ends 2027-10-30; term 2 2027-10-31 to 2028-10-30;
//   start 2026-12-12 ends 2027-12-11; term 2 2027-12-12 to 2028-12-11;
//   start 2026-11-04 ends 2027-11-03 (rejected: one term);
//   start 2026-11-14 ends 2027-11-13 (rejected: one term).
public sealed class RenewAmountTests : IClassFixture<PoliciesApiFactory>
{
    private const string Title = "Renewal amount must be greater than zero";

    private readonly PoliciesApiFactory _factory;

    public RenewAmountTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_of_one_penny_is_accepted()
    {
        const string reference = "RENEW5-ONE-PENNY";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 10, 31)).WithAmount(415.80m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 15));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(0.01m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 10, 31), new DateOnly(2027, 10, 30), "415.80", "DirectDebit"),
                new ExpectedPolicyTerm(new DateOnly(2027, 10, 31), new DateOnly(2028, 10, 30), "0.01", "DirectDebit"),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_of_1_000_000_00_is_accepted()
    {
        const string reference = "RENEW5-ONE-MILLION";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 12)).WithAmount(640.25m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 28));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(1_000_000.00m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 12, 12), new DateOnly(2027, 12, 11), "640.25", "Card"),
                new ExpectedPolicyTerm(new DateOnly(2027, 12, 12), new DateOnly(2028, 12, 11), "1000000.00", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_of_zero_is_rejected_with_422()
    {
        const string reference = "RENEW5-ZERO";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 4)).WithAmount(402.17m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 21));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(0.00m).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "RENEW-5", Title, RenewalActions.RenewalsPath(reference));
        CancellationAssert.Money(problem, "amount", "0.00");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 3), "Active");
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Negative_renewal_amount_is_rejected_with_422()
    {
        const string reference = "RENEW5-NEGATIVE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 14)).WithAmount(421.09m).WithPaymentMethod("DirectDebit").Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 29));

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(-25.50m).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "RENEW-5", Title, RenewalActions.RenewalsPath(reference));
        CancellationAssert.Money(problem, "amount", "-25.50");
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, new DateOnly(2027, 11, 13), "Active");
    }
}
