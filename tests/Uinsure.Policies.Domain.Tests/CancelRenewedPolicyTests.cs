using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// CANCEL-5: cancelling a renewed policy ends it. Every term is calculated: the current term by the normal rules,
// a future term in full (CANCEL-1a, it is before its start). Each refund goes back to the payment it reverses,
// and a renewed term's refund is at most its payment, which is for the term's amount. No £0 Refund is created;
// an unpaid renewed term (RENEW-3b) is UNPAID-TERM with 0.00 and no refund. The top-level rule is the current
// term's. Each figure is a literal worked out by hand. The builder's default sale, then a real Policy.Renew:
//   term 1 2026-11-08 to 2027-11-07 (365 days, 527.18 by DirectDebit, PAY-DOM-0042);
//   term 2 2027-11-08 to 2028-11-07 (366 days, 560.00 by Card, PAY-DOM-R0001), renewed on 2027-10-20 or
//   on the end date 2027-11-07.
public sealed class CancelRenewedPolicyTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);

    private static Policy Renewed(PolicyBuilder builder, PaymentMethod? method, DateOnly renewedOn)
    {
        var policy = builder.Build();
        Assert.True(policy.Renew(560.00m, method, renewedOn, new PaymentReferences().Next).IsSuccess);
        return policy;
    }

    // Cancelled on the renewal day, 2027-10-20: term 1 has 347 days used and 18 unused,
    // 527.18 × 18 ÷ 365 = 25.9979… → 26.00 (CANCEL-1c); term 2 has not started, 560.00 in full (CANCEL-1a).
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    public void Cancelling_a_renewed_policy_refunds_the_current_term_pro_rata_and_the_future_term_in_full()
    {
        var policy = Renewed(new PolicyBuilder(), PaymentMethod.Card, RenewalDay);

        var result = policy.Cancel(RenewalDay, RenewalDay, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value,
            RenewalDay,
            "CANCEL-1c",
            586.00m,
            [new RefundCalculation(26.00m, "CANCEL-1c", 365, 347, 18), new RefundCalculation(560.00m, "CANCEL-1a", 366, 0, 366)],
            [
                new Refund("REF-DOM-0001", PaymentMethod.DirectDebit, 26.00m, "PAY-DOM-0042"),
                new Refund("REF-DOM-0002", PaymentMethod.Card, 560.00m, "PAY-DOM-R0001"),
            ]);
        Assert.Equal(PolicyStatus.Cancelled, policy.StatusOn(RenewalDay));
    }

    // Cancelled with effect from term 1's last day, 2027-11-07: term 1 is fully used (0.00, no refund); term 2
    // starts the next day, 560.00 in full. The top-level rule is term 1's, the term covering that date.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    public void Cancelling_a_renewed_policy_from_the_current_terms_last_day_refunds_only_the_future_term()
    {
        var policy = Renewed(new PolicyBuilder(), PaymentMethod.Card, RenewalDay);

        var result = policy.Cancel(new DateOnly(2027, 11, 7), RenewalDay, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value,
            new DateOnly(2027, 11, 7),
            "CANCEL-1c",
            560.00m,
            [new RefundCalculation(0.00m, "CANCEL-1c", 365, 365, 0), new RefundCalculation(560.00m, "CANCEL-1a", 366, 0, 366)],
            [new Refund("REF-DOM-0001", PaymentMethod.Card, 560.00m, "PAY-DOM-R0001")]);
    }

    // Renewed on the end date, cancelled on 2027-11-10, start + 2 of term 2: term 2 is the current term and in
    // cooling-off, 560.00 in full (CANCEL-1b), with 3 days used; term 1 is fully used (0.00, no refund).
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    public void Cancelling_a_renewed_policy_in_the_renewed_terms_cooling_off_refunds_it_in_full()
    {
        var policy = Renewed(new PolicyBuilder(), PaymentMethod.Card, new DateOnly(2027, 11, 7));
        var today = new DateOnly(2027, 11, 10);

        var result = policy.Cancel(today, today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value,
            today,
            "CANCEL-1b",
            560.00m,
            [new RefundCalculation(0.00m, "CANCEL-1c", 365, 365, 0), new RefundCalculation(560.00m, "CANCEL-1b", 366, 3, 363)],
            [new Refund("REF-DOM-0001", PaymentMethod.Card, 560.00m, "PAY-DOM-R0001")]);
    }

    // Cancelled on 2028-05-08, mid term 2: 183 days used and 183 unused, 560.00 × 183 ÷ 366 = 280.00 (CANCEL-1c),
    // back to term 2's Card payment; term 1 is fully used.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    public void Cancelling_a_renewed_policy_mid_renewed_term_refunds_it_pro_rata_to_its_own_payment()
    {
        var policy = Renewed(new PolicyBuilder(), PaymentMethod.Card, RenewalDay);
        var today = new DateOnly(2028, 5, 8);

        var result = policy.Cancel(today, today, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value,
            today,
            "CANCEL-1c",
            280.00m,
            [new RefundCalculation(0.00m, "CANCEL-1c", 365, 365, 0), new RefundCalculation(280.00m, "CANCEL-1c", 366, 183, 183)],
            [new Refund("REF-DOM-0001", PaymentMethod.Card, 280.00m, "PAY-DOM-R0001")]);
    }

    // Without auto-renew, term 2 is unpaid: cancelled on 2027-10-20, it is UNPAID-TERM with 0.00 and no refund;
    // term 1 refunds 26.00 as above.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "RENEW-3b")]
    public void Cancelling_a_policy_renewed_unpaid_refunds_nothing_for_the_unpaid_term()
    {
        var policy = Renewed(new PolicyBuilder().WithAutoRenew(false), null, RenewalDay);

        var result = policy.Cancel(RenewalDay, RenewalDay, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        CancellationAssert.Is(
            result.Value,
            RenewalDay,
            "CANCEL-1c",
            26.00m,
            [new RefundCalculation(26.00m, "CANCEL-1c", 365, 347, 18), new RefundCalculation(0.00m, "UNPAID-TERM", 366, 0, 366)],
            [new Refund("REF-DOM-0001", PaymentMethod.DirectDebit, 26.00m, "PAY-DOM-0042")]);
    }

    // The renewed term's refund (560.00 in full) is exactly its payment: never more than was paid.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    public void Cancelling_a_renewed_policy_refunds_the_renewed_term_no_more_than_its_payment()
    {
        var policy = Renewed(new PolicyBuilder(), PaymentMethod.Card, RenewalDay);

        var result = policy.Cancel(RenewalDay, RenewalDay, new RefundReferences().Next);

        Assert.True(result.IsSuccess);
        var payment = policy.Terms[1].Payment;
        Assert.NotNull(payment);
        var refund = Assert.Single(result.Value.Refunds, r => r.PaymentReference == payment.Reference);
        Assert.Equal(560.00m, payment.Amount);
        Assert.Equal(560.00m, refund.Amount);
    }
}
