using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// RENEW-4: a renewal appends the next term, starting the day after the latest term's end, for the amount sent,
// under the same reference; the terms before it are left as they were, and the term returned is the new latest.
// Renewed on 2027-10-20, inside the window of the builder's default sale (reference DOM-0042):
//   term 1 2026-11-08 to 2027-11-07 (527.18, PAY-DOM-0042); term 2 2027-11-08 to 2028-11-07 (366 days);
//   renewed again on 2028-10-20, term 3 2028-11-08 to 2029-11-07.
// RestoredPolicy.CardThenDirectDebit ends 2028-02-29; renewed on 2028-02-01, term 3 runs 2028-03-01 to 2029-02-28.
public sealed class RenewTermTests
{
    private static readonly DateOnly RenewalDay = new(2027, 10, 20);

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public void Renewal_appends_a_term_from_the_day_after_the_latest_end_under_the_same_reference()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(574.83m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, policy.Terms.Count);
        TermAssert.Is(policy.Terms[0], new DateOnly(2026, 11, 8), new DateOnly(2027, 11, 7), 527.18m,
            new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m));
        TermAssert.Is(policy.Terms[1], new DateOnly(2027, 11, 8), new DateOnly(2028, 11, 7), 574.83m,
            new Payment("PAY-DOM-R0001", PaymentMethod.DirectDebit, 574.83m));
        Assert.Same(policy.LatestTerm, result.Value);
        Assert.Equal(new PolicyReference("DOM-0042"), policy.Reference);
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public void Renewed_term_runs_366_days_through_29_February()
    {
        var policy = new PolicyBuilder().Build();

        var result = policy.Renew(574.83m, null, RenewalDay, new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(366, result.Value.Days);
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public void Second_renewal_appends_a_third_term_from_the_day_after_the_second_ends()
    {
        var policy = new PolicyBuilder().Build();
        var references = new PaymentReferences();
        Assert.True(policy.Renew(574.83m, null, RenewalDay, references.Next).IsSuccess);

        var result = policy.Renew(590.26m, null, new DateOnly(2028, 10, 20), references.Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, policy.Terms.Count);
        Assert.Equal(new DateOnly(2027, 11, 8), policy.Terms[1].Start);
        TermAssert.Is(policy.Terms[2], new DateOnly(2028, 11, 8), new DateOnly(2029, 11, 7), 590.26m,
            new Payment("PAY-DOM-R0002", PaymentMethod.DirectDebit, 590.26m));
        Assert.Same(policy.LatestTerm, result.Value);
    }

    // The latest term ends on 29 Feb 2028, so the next starts on 1 Mar 2028 and ends 28 Feb 2029 (365 days).
    [Fact]
    [Trait("Rule", "RENEW-4")]
    public void Renewal_of_a_term_ending_29_February_starts_on_1_March()
    {
        var policy = RestoredPolicy.CardThenDirectDebit();

        var result = policy.Renew(538.90m, PaymentMethod.Card, new DateOnly(2028, 2, 1), new PaymentReferences().Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, policy.Terms.Count);
        TermAssert.Is(policy.Terms[2], new DateOnly(2028, 3, 1), new DateOnly(2029, 2, 28), 538.90m,
            new Payment("PAY-DOM-R0001", PaymentMethod.Card, 538.90m));
    }

    // While term 1 is still running (today 2027-10-20), the renewed term is in the future: the policy is still
    // active, its current term is term 1, and term 1's dates are unchanged.
    [Fact]
    [Trait("Rule", "RENEW-4")]
    public void Renewal_during_the_current_term_leaves_it_current_and_the_policy_active()
    {
        var policy = new PolicyBuilder().Build();

        Assert.True(policy.Renew(574.83m, null, RenewalDay, new PaymentReferences().Next).IsSuccess);

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(RenewalDay));
        Assert.Same(policy.Terms[0], policy.CurrentTerm(RenewalDay));
        Assert.Equal(new DateOnly(2027, 11, 7), policy.Terms[0].End);
    }
}
