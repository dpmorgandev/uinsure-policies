using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// CANCEL-5: cancelling a renewed policy ends it. Every term is listed in the cancellation: the current term (the
// one covering the cancellation date) follows the normal rules, a future term is refunded in full (CANCEL-1a, it
// is before its start), and a term already over is fully used (0.00). The top-level ruleApplied is the current
// term's. A refund is made for each paid term with an amount above 0, to the method of the payment it reverses
// (CANCEL-2), and an unpaid renewed term (RENEW-3b) is UNPAID-TERM with 0.00 and no refund. Each policy is sold
// (E1) and renewed (E6) over HTTP, cancelled (E3) or quoted (E4), and read back whole (E2). Every refund is a
// literal; each pro-rata one has more than 2 dp before Money.Round:
//   sold 2026-11-03 for 487.39 by Direct Debit (term 1 to 2027-11-02, 365 days), renewed on 2027-10-12 for
//   512.66 by Card (term 2 2027-11-03 to 2028-11-02, 366 days), cancelled on 2027-10-20:
//   term 1 CANCEL-1c 352 used, 13 unused, 487.39 x 13 / 365 = 17.3590... to 17.36; term 2 CANCEL-1a 512.66;
//   sold 2026-11-17 for 529.84 by Card (to 2027-11-16), renewed on 2027-11-01 for 548.27 (term 2 2027-11-17 to
//   2028-11-16), cancelled today 2027-12-01, term 2's start + 14: term 1 used up, 0.00; term 2 CANCEL-1b 548.27;
//   sold 2026-12-08 for 573.15 by Direct Debit (to 2027-12-07), renewed on 2027-11-20 for 596.42 by Card (term 2
//   2027-12-08 to 2028-12-07), cancelled on 2027-12-23, term 2's start + 15: term 1 0.00; term 2 CANCEL-1c
//   16 used, 350 unused, 596.42 x 350 / 366 = 570.3469... to 570.35;
//   sold 2026-11-11 for 446.27 by Card without auto-renew (to 2027-11-10), renewed unpaid on 2027-10-22 for 470.18
//   (term 2 2027-11-11 to 2028-11-10), cancelled on 2027-10-31: term 1 CANCEL-1c 355 used, 10 unused,
//   446.27 x 10 / 365 = 12.2265... to 12.23; term 2 UNPAID-TERM 0.00;
//   sold 2026-11-26 for 437.92 by Direct Debit without auto-renew (to 2027-11-25), renewed unpaid on 2027-11-08 for
//   459.06 (term 2 2027-11-26 to 2028-11-25), quoted for 2027-11-15: term 1 CANCEL-1c 355 used, 10 unused,
//   437.92 x 10 / 365 = 11.9978... to 12.00; term 2 UNPAID-TERM 0.00.
public sealed class CancelRenewedPolicyTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public CancelRenewedPolicyTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "CANCEL-1c")]
    [Trait("Rule", "CANCEL-1a")]
    public async Task Cancelling_in_term_1_after_a_renewal_refunds_term_1_pro_rata_and_the_future_term_2_in_full()
    {
        const string reference = "CAN5-FUTURE-TERM";
        var cancelOn = new DateOnly(2027, 10, 20);
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 3)).WithAmount(487.39m).WithPaymentMethod("DirectDebit").Build();
        ExpectedPolicyTerm[] terms =
        [
            new(new DateOnly(2026, 11, 3), new DateOnly(2027, 11, 2), "487.39", "DirectDebit"),
            new(new DateOnly(2027, 11, 3), new DateOnly(2028, 11, 2), "512.66", "Card"),
        ];
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 12));
        var payments = await client.RenewAndReadBack(
            reference,
            new RenewalRequestBuilder().WithAmount(512.66m).WithPaymentMethod("Card").Build(),
            sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, terms),
            [sale.PaymentReference()]);

        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancelOn).Build());

        var refunds = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, cancelOn, "CANCEL-1c", "530.02",
            [
                new ExpectedTerm(1, "CANCEL-1c", 365, 352, 13, "17.36"),
                new ExpectedTerm(2, "CANCEL-1a", 366, 0, 366, "512.66"),
            ],
            [
                new ExpectedRefund("DirectDebit", "17.36", payments[0]!),
                new ExpectedRefund("Card", "512.66", payments[1]!),
            ]));
        RenewedPolicyAssert.ReadsBack(
            await client.ReadBack(reference),
            sold,
            new ExpectedRenewedPolicy(
                reference, "Cancelled", 1, terms,
                new ExpectedCancellationSummary(cancelOn, "CANCEL-1c", "530.02"),
                [
                    new ExpectedPolicyRefund(refunds[0], "DirectDebit", "17.36", payments[0]!),
                    new ExpectedPolicyRefund(refunds[1], "Card", "512.66", payments[1]!),
                ]),
            payments);
    }

    // The renewed term is current and inside its own cooling-off period; term 1 is over, so it refunds nothing and
    // no £0 refund is made for it.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "CANCEL-1b")]
    public async Task Cancelling_on_start_plus_14_of_the_renewed_term_refunds_it_in_full_and_nothing_for_term_1()
    {
        const string reference = "CAN5-TERM2-COOLING";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 17)).WithAmount(529.84m).WithPaymentMethod("Card").Build();
        ExpectedPolicyTerm[] terms =
        [
            new(new DateOnly(2026, 11, 17), new DateOnly(2027, 11, 16), "529.84", "Card"),
            new(new DateOnly(2027, 11, 17), new DateOnly(2028, 11, 16), "548.27", "Card"),
        ];
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 1));
        var payments = await client.RenewAndReadBack(
            reference, new RenewalRequestBuilder().WithAmount(548.27m).Build(), sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, terms), [sale.PaymentReference()]);
        _factory.MoveClockTo(new DateOnly(2027, 12, 1));

        using var response = await client.PostCancellationWithNoBody(reference);

        var refunds = CancellationAssert.IsCancellation(await PolicyActions.CancelledBody(response), new ExpectedCancellation(
            reference, new DateOnly(2027, 12, 1), "CANCEL-1b", "548.27",
            [
                new ExpectedTerm(1, "CANCEL-1c", 365, 365, 0, "0.00"),
                new ExpectedTerm(2, "CANCEL-1b", 366, 15, 351, "548.27"),
            ],
            [new ExpectedRefund("Card", "548.27", payments[1]!)]));
        RenewedPolicyAssert.ReadsBack(
            await client.ReadBack(reference),
            sold,
            new ExpectedRenewedPolicy(
                reference, "Cancelled", 2, terms,
                new ExpectedCancellationSummary(new DateOnly(2027, 12, 1), "CANCEL-1b", "548.27"),
                [new ExpectedPolicyRefund(Assert.Single(refunds), "Card", "548.27", payments[1]!)]),
            payments);
    }

    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "CANCEL-1c")]
    public async Task Cancelling_on_start_plus_15_of_the_renewed_term_refunds_it_pro_rata_over_366_days()
    {
        const string reference = "CAN5-TERM2-PRO-RATA";
        var cancelOn = new DateOnly(2027, 12, 23);
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 12, 8)).WithAmount(573.15m).WithPaymentMethod("DirectDebit").Build();
        ExpectedPolicyTerm[] terms =
        [
            new(new DateOnly(2026, 12, 8), new DateOnly(2027, 12, 7), "573.15", "DirectDebit"),
            new(new DateOnly(2027, 12, 8), new DateOnly(2028, 12, 7), "596.42", "Card"),
        ];
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 20));
        var payments = await client.RenewAndReadBack(
            reference, new RenewalRequestBuilder().WithAmount(596.42m).WithPaymentMethod("Card").Build(), sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, terms), [sale.PaymentReference()]);
        _factory.MoveClockTo(new DateOnly(2027, 12, 20));

        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancelOn).Build());

        var refunds = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, cancelOn, "CANCEL-1c", "570.35",
            [
                new ExpectedTerm(1, "CANCEL-1c", 365, 365, 0, "0.00"),
                new ExpectedTerm(2, "CANCEL-1c", 366, 16, 350, "570.35"),
            ],
            [new ExpectedRefund("Card", "570.35", payments[1]!)]));
        RenewedPolicyAssert.ReadsBack(
            await client.ReadBack(reference),
            sold,
            new ExpectedRenewedPolicy(
                reference, "Cancelled", 2, terms,
                new ExpectedCancellationSummary(cancelOn, "CANCEL-1c", "570.35"),
                [new ExpectedPolicyRefund(Assert.Single(refunds), "Card", "570.35", payments[1]!)]),
            payments);
    }

    // The renewed term was created unpaid (RENEW-3b), so there is no payment to reverse: UNPAID-TERM, 0.00 and no
    // refund for it, although it is a future term.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "RENEW-3b")]
    public async Task Cancelling_a_policy_whose_renewed_term_is_unpaid_refunds_nothing_for_that_term()
    {
        const string reference = "CAN5-UNPAID-TERM";
        var cancelOn = new DateOnly(2027, 10, 31);
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 11)).WithAmount(446.27m).WithAutoRenew(false).WithPaymentMethod("Card").Build();
        ExpectedPolicyTerm[] terms =
        [
            new(new DateOnly(2026, 11, 11), new DateOnly(2027, 11, 10), "446.27", "Card"),
            new(new DateOnly(2027, 11, 11), new DateOnly(2028, 11, 10), "470.18", null),
        ];
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 10, 22));
        var payments = await client.RenewAndReadBack(
            reference, new RenewalRequestBuilder().WithAmount(470.18m).Build(), sold,
            new ExpectedRenewedPolicy(reference, "Active", 1, terms), [sale.PaymentReference()]);

        var body = await client.Cancel(reference, new CancellationRequestBuilder().WithDate(cancelOn).Build());

        var refunds = CancellationAssert.IsCancellation(body, new ExpectedCancellation(
            reference, cancelOn, "CANCEL-1c", "12.23",
            [
                new ExpectedTerm(1, "CANCEL-1c", 365, 355, 10, "12.23"),
                new ExpectedTerm(2, "UNPAID-TERM", 366, 0, 366, "0.00"),
            ],
            [new ExpectedRefund("Card", "12.23", payments[0]!)]));
        RenewedPolicyAssert.ReadsBack(
            await client.ReadBack(reference),
            sold,
            new ExpectedRenewedPolicy(
                reference, "Cancelled", 1, terms,
                new ExpectedCancellationSummary(cancelOn, "CANCEL-1c", "12.23"),
                [new ExpectedPolicyRefund(Assert.Single(refunds), "Card", "12.23", payments[0]!)]),
            payments);
    }

    // E4 on a renewed policy quotes the same per-term decision, with quotedOn UK today, and changes nothing: the
    // read-back still has both terms and no cancellation.
    [Fact]
    [Trait("Rule", "CANCEL-5")]
    [Trait("Rule", "RENEW-3b")]
    [Trait("Rule", "COULD-1")]
    public async Task Quote_for_a_policy_whose_renewed_term_is_unpaid_quotes_nothing_for_that_term_and_changes_nothing()
    {
        const string reference = "CAN5-UNPAID-QUOTE";
        var sold = new SellPolicyRequestBuilder(reference)
            .WithStartDate(new DateOnly(2026, 11, 26)).WithAmount(437.92m).WithAutoRenew(false).WithPaymentMethod("DirectDebit").Build();
        var renewed = new ExpectedRenewedPolicy(reference, "Active", 1, [
            new ExpectedPolicyTerm(new DateOnly(2026, 11, 26), new DateOnly(2027, 11, 25), "437.92", "DirectDebit"),
            new ExpectedPolicyTerm(new DateOnly(2027, 11, 26), new DateOnly(2028, 11, 25), "459.06", null),
        ]);
        using var client = _factory.CreateClient();
        var sale = await client.SellAndReadBack(sold, reference);
        _factory.MoveClockTo(new DateOnly(2027, 11, 8));
        var payments = await client.RenewAndReadBack(
            reference, new RenewalRequestBuilder().WithAmount(459.06m).Build(), sold, renewed, [sale.PaymentReference()]);

        var quote = await client.Quote(reference, new DateOnly(2027, 11, 15));

        CancellationAssert.IsQuote(quote, new ExpectedCancellation(
            reference, new DateOnly(2027, 11, 15), "CANCEL-1c", "12.00",
            [
                new ExpectedTerm(1, "CANCEL-1c", 365, 355, 10, "12.00"),
                new ExpectedTerm(2, "UNPAID-TERM", 366, 0, 366, "0.00"),
            ],
            [new ExpectedRefund("DirectDebit", "12.00", payments[0]!)]),
            new DateOnly(2027, 11, 8));
        RenewedPolicyAssert.ReadsBack(await client.ReadBack(reference), sold, renewed, payments);
    }
}
