using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-2: each refund goes to the method of the payment it reverses, and names that payment. The sold rows
// (Card, Cheque, Direct Debit) are in CancelBeforeStartTests and CancelCoolingOffTests; this row starts from a
// payment whose reference is a known literal, so the refund's paymentReference can be checked against it and
// not only against the sale's read-back. The policy is seeded through the app's IPolicyRepository
// (PolicyBuilder, PolicySeeder) with the PolicyBuilder defaults: BuyToLet, auto-renew, Mary Jackson,
// 31 Deansgate, Floor 4, M3 2BA, and one term 2026-09-07 to 2027-09-06 (365 days), in force on 2026-10-17,
// for 446.50 paid by Direct Debit as PAY-CAN2-SEEDED-1.
public sealed class CancelRefundMethodTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public CancelRefundMethodTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    // 2027-01-15: 131 days used, 234 unused; 446.50 × 234 ÷ 365 = 286.24931… → 286.25, by Direct Debit,
    // reversing PAY-CAN2-SEEDED-1.
    [Fact]
    [Trait("Rule", "CANCEL-2")]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Refund_goes_to_the_method_of_the_payment_it_reverses_and_names_that_payment()
    {
        const string reference = "CAN2-SEEDED";
        var start = new DateOnly(2026, 9, 7);
        await _factory.Seed(new PolicyBuilder(reference)
            .WithTerms(new Term(start, 446.50m, new Payment("PAY-CAN2-SEEDED-1", PaymentMethod.DirectDebit, 446.50m)))
            .Build());
        using var client = _factory.CreateClient();

        using var response = await client.PostCancellation(reference, new CancellationRequestBuilder().WithDate(new DateOnly(2027, 1, 15)).Build());

        await OneTermCancellation.AssertCancelledAndReadBack(
            client, response, reference, SeededSale.For(reference, start, 446.50m, "DirectDebit"), new DateOnly(2027, 9, 6),
            "PAY-CAN2-SEEDED-1", new DateOnly(2027, 1, 15), "CANCEL-1c", 365, 131, 234, "286.25", "DirectDebit");
    }
}
