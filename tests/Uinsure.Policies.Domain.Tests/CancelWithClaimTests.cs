using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// COULD-2: a policy with a claim is refunded 0 when cancelled, whichever of CANCEL-1a, 1b or 1c would apply. The
// term's RuleApplied is COULD-2, its day counts are as usual, and no £0 Refund is created. UNPAID-TERM (a
// ruleApplied value, not a rule ID: a term with no payment, so nothing to reverse) takes precedence over COULD-2.
//   start 2026-11-08 ends 2027-11-07; start 2026-10-17 (today) ends 2027-10-16 (both 365 days).
public sealed class CancelWithClaimTests
{
    private static readonly DateOnly Today = TestClock.Today;
    private static readonly DateOnly Start = new(2026, 11, 8);

    private static Term PaidTerm() => new(Start, 527.18m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m));

    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Calculation_with_a_claim_before_the_start_is_nothing_overriding_CANCEL_1a()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(), new DateOnly(2026, 11, 7), hasClaims: true);

        Assert.Equal(new RefundCalculation(0m, "COULD-2", 365, 0, 365), calculation);
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Calculation_with_a_claim_on_start_plus_14_is_nothing_overriding_CANCEL_1b()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(), new DateOnly(2026, 11, 22), hasClaims: true);

        Assert.Equal(new RefundCalculation(0m, "COULD-2", 365, 15, 350), calculation);
    }

    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Calculation_with_a_claim_on_start_plus_15_is_nothing_overriding_CANCEL_1c()
    {
        var calculation = RefundCalculator.ForTerm(PaidTerm(), new DateOnly(2026, 11, 23), hasClaims: true);

        Assert.Equal(new RefundCalculation(0m, "COULD-2", 365, 16, 349), calculation);
    }

    // No payment, nothing to reverse. UNPAID-TERM is not a rule ID, so this carries no trait.
    [Fact]
    public void Calculation_for_an_unpaid_term_is_nothing()
    {
        var calculation = RefundCalculator.ForTerm(new Term(Start, 527.18m, null), new DateOnly(2026, 11, 23), hasClaims: false);

        Assert.Equal(new RefundCalculation(0m, "UNPAID-TERM", 365, 16, 349), calculation);
    }

    // COULD-2 does not override UNPAID-TERM: the term had no payment to withhold.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Calculation_for_an_unpaid_term_with_a_claim_is_UNPAID_TERM_not_COULD_2()
    {
        var calculation = RefundCalculator.ForTerm(new Term(Start, 527.18m, null), new DateOnly(2026, 11, 7), hasClaims: true);

        Assert.Equal(new RefundCalculation(0m, "UNPAID-TERM", 365, 0, 365), calculation);
    }

    // Claimed today on the start date, then cancelled on start + 14: cooling-off would refund 275.90 in full.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Cancel_in_cooling_off_after_a_claim_refunds_nothing_and_creates_no_refund()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).WithAmount(275.90m).Build();
        Assert.True(policy.RecordClaim(Today, "Storm damage to the garage roof", Today).IsSuccess);

        var result = policy.Cancel(new DateOnly(2026, 10, 31), Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2026, 10, 31), "COULD-2", 0m,
            [new RefundCalculation(0m, "COULD-2", 365, 15, 350)],
            []);
    }

    // As above, cancelled on start + 15: pro-rata would refund 263.81.
    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Cancel_after_cooling_off_after_a_claim_refunds_nothing()
    {
        var policy = new PolicyBuilder().WithStartDate(Today).WithAmount(275.90m).Build();
        Assert.True(policy.RecordClaim(Today, "Storm damage to the garage roof", Today).IsSuccess);

        var result = policy.Cancel(new DateOnly(2026, 11, 1), Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, new DateOnly(2026, 11, 1), "COULD-2", 0m,
            [new RefundCalculation(0m, "COULD-2", 365, 16, 349)],
            []);
    }

    // RestoredPolicy.CardThenDirectDebit, claimed in term 1: every paid term's refund is withheld, including
    // term 2's, which hasn't started (CANCEL-1a would refund it in full).
    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Claim_on_a_two_term_policy_refunds_nothing_on_either_term()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();
        Assert.True(policy.RecordClaim(new DateOnly(2026, 10, 1), "Bicycle stolen from the shed", Today).IsSuccess);

        var result = policy.Cancel(Today, Today, new RefundReferences().Next);

        CancellationAssert.Is(
            result.Value, Today, "COULD-2", 0m,
            [new RefundCalculation(0m, "COULD-2", 365, 231, 134), new RefundCalculation(0m, "COULD-2", 366, 0, 366)],
            []);
    }
}
