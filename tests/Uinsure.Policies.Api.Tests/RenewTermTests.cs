using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// RENEW-4: a renewal appends the next term under the same reference, starting the day after the latest term's
// end; the earlier terms are kept as they were. E6 answers 201 with Location /policies/{reference} and the
// PolicyResponse, and the GET read-back is asserted whole (RenewedPolicyAssert). The renewal window is measured
// from the latest term's end (RENEW-1/2), so a policy renewed once cannot be renewed again until the window of
// its new term opens. Each policy is sold (E1) with the clock at TestClock.Now and then renewed later; every
// date is a literal:
//   start 2026-11-30 ends 2027-11-29; term 2 2027-11-30 to 2028-11-29;
//   start 2026-11-21 ends 2027-11-20; term 2 2027-11-21 to 2028-11-20;
//   start 2026-12-05 ends 2027-12-04; term 2 2027-12-05 to 2028-12-04, whose window is 2028-11-04 to 2028-12-04;
//   start 2026-11-12 ends 2027-11-11; term 2 2027-11-12 to 2028-11-11; term 3 2028-11-12 to 2029-11-11;
//   start 2026-12-01 ends 2027-11-30; term 2 2027-12-01 to 2028-11-30.
public sealed class RenewTermTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public RenewTermTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewal_appends_term_2_from_the_day_after_term_1_ends_under_the_same_reference()
    {
        const string reference = "RENEW4-APPEND";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithType("BuyToLet")
            .WithStartDate(new DateOnly(2026, 11, 30))
            .WithAmount(562.91m)
            .WithPaymentMethod("DirectDebit")
            .WithPolicyholders(
                new PolicyholderBuilder().WithName("Dorothy", "Vaughan").WithDateOfBirth(new DateOnly(1979, 9, 20)),
                new PolicyholderBuilder().WithName("Mary", "Winston").WithDateOfBirth(new DateOnly(1983, 2, 14)))
            .WithAddress("12 Oldham St", null, "Northern Quarter")
            .WithPostcode("M1 1JQ")
            .Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 10));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(588.04m).WithPaymentMethod("Card").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 11, 30), new DateOnly(2027, 11, 29), "562.91", "DirectDebit"),
                new ExpectedPolicyTerm(new DateOnly(2027, 11, 30), new DateOnly(2028, 11, 29), "588.04", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    // On the renewed term's first day it is the current term: the policy-level dates and amount are term 2's.
    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewed_policy_shows_term_2_as_current_from_its_start_date()
    {
        const string reference = "RENEW4-TERM2-CURRENT";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 21)).WithAmount(509.33m).WithPaymentMethod("Card").Build();
        ExpectedPolicyTerm[] terms =
        [
            new(new DateOnly(2026, 11, 21), new DateOnly(2027, 11, 20), "509.33", "Card"),
            new(new DateOnly(2027, 11, 21), new DateOnly(2028, 11, 20), "534.80", "DirectDebit"),
        ];
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 1));
        var paymentReferences = await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(534.80m).WithPaymentMethod("DirectDebit").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, terms),
            [sale.PaymentReference()]);

        _factory.MoveClockTo(new DateOnly(2027, 11, 21));

        RenewedPolicyAssert.ReadsBack(
            await client.ReadBack(reference), sold, new ExpectedRenewedPolicy(reference, "Active", 2, terms), paymentReferences);
    }

    // Renewed on 2027-11-20, the latest term is now term 2, ending 2028-12-04: a second renewal the same day is
    // a year before that term's window and is rejected, and the policy keeps its two terms.
    [Fact]
    [Trait("Rule", "RENEW-4")]
    [Trait("Rule", "RENEW-1")]
    public async Task Second_renewal_on_the_same_day_is_outside_the_window_of_the_latest_term()
    {
        const string reference = "RENEW4-LATEST-WINDOW";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 5)).WithAmount(473.60m).WithPaymentMethod("DirectDebit").Build();
        var expected = new ExpectedRenewedPolicy(reference, "Active", 1, [
            new ExpectedPolicyTerm(new DateOnly(2026, 12, 5), new DateOnly(2027, 12, 4), "473.60", "DirectDebit"),
            new ExpectedPolicyTerm(new DateOnly(2027, 12, 5), new DateOnly(2028, 12, 4), "497.15", "DirectDebit"),
        ]);
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 20));
        var paymentReferences = await client.RenewAndReadBack(
            reference, new RenewalRequestBuilder().WithAmount(497.15m).Build(), sold, expected, [sale.PaymentReference()]);

        using var response = await client.PostRenewal(reference, new RenewalRequestBuilder().WithAmount(501.42m).Build());

        var problem = await ProblemAssert.SingleRuleViolation(response, "RENEW-1/2", "Outside the renewal window", RenewalActions.RenewalsPath(reference));
        Assert.Equal(new DateOnly(2028, 11, 4), problem.GetDate("windowStart"));
        Assert.Equal(new DateOnly(2028, 12, 4), problem.GetDate("windowEnd"));
        RenewedPolicyAssert.ReadsBack(await client.ReadBack(reference), sold, expected, paymentReferences);
    }

    // Inside term 2's window (2028-10-12 to 2028-11-11) a second renewal appends term 3. With no paymentMethod it
    // is paid by the current term's method, term 2's Direct Debit, not term 1's Card (RENEW-3a).
    [Fact]
    [Trait("Rule", "RENEW-4")]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_in_the_window_of_the_renewed_term_appends_term_3_paid_by_the_current_terms_method()
    {
        const string reference = "RENEW4-THIRD-TERM";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 12)).WithAmount(455.18m).WithPaymentMethod("Card").Build();
        var term1 = new ExpectedPolicyTerm(new DateOnly(2026, 11, 12), new DateOnly(2027, 11, 11), "455.18", "Card");
        var term2 = new ExpectedPolicyTerm(new DateOnly(2027, 11, 12), new DateOnly(2028, 11, 11), "468.90", "DirectDebit");
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 25));
        var paymentReferences = await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(468.90m).WithPaymentMethod("DirectDebit").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [term1, term2]),
            [sale.PaymentReference()]);
        _factory.MoveClockTo(new DateOnly(2028, 11, 1));

        await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(481.27m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 2, [
                term1,
                term2,
                new ExpectedPolicyTerm(new DateOnly(2028, 11, 12), new DateOnly(2029, 11, 11), "481.27", "DirectDebit"),
            ]),
            paymentReferences);
    }

    // The route reference is trimmed and upper-cased before the lookup, as on every route; Location names the
    // normalised reference.
    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewal_by_the_lower_case_reference_renews_the_policy()
    {
        const string reference = "RENEW4-LOWER";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 1)).WithAmount(471.56m).WithPaymentMethod("Card").Build();
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 15));

        await client.RenewAndReadBack(
            "renew4-lower",
            new RenewalRequestBuilder().WithAmount(489.67m).Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, [
                new ExpectedPolicyTerm(new DateOnly(2026, 12, 1), new DateOnly(2027, 11, 30), "471.56", "Card"),
                new ExpectedPolicyTerm(new DateOnly(2027, 12, 1), new DateOnly(2028, 11, 30), "489.67", "Card"),
            ]),
            [sale.PaymentReference()]);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Renewal_of_an_unknown_reference_answers_404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostRenewal("RENEW-NO-SUCH", new RenewalRequestBuilder().Build());

        await ProblemAssert.NotFound(response, RenewalActions.RenewalsPath("RENEW-NO-SUCH"), "RENEW-NO-SUCH");
    }
}
