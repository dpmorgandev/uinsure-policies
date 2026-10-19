using System.Net;
using System.Text.Json;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1: the policy-level startDate, endDate and amount (and currentTermNumber) are the current term's, the
// term in force on UK today, or the latest term when today is outside them all; terms[] holds the history.
// Only a renewal makes a second term, and renewals are F4's, so each policy is seeded through the app's
// IPolicyRepository (PolicyBuilder, PolicySeeder) and read back over HTTP:
//   term 1: 2026-03-02 to 2027-03-01, 486.20 by Card;
//   term 2: 2027-03-02 to 2028-03-01, 512.75 by DirectDebit (unpaid in the RENEW-3b-shaped row).
// Each side of the term boundary is its own test. The end dates are literals.
public sealed class RetrieveCurrentTermTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly Term1Start = new(2026, 3, 2);
    private static readonly DateOnly Term1End = new(2027, 3, 1);
    private static readonly DateOnly Term2Start = new(2027, 3, 2);
    private static readonly DateOnly Term2End = new(2028, 3, 1);

    private readonly PoliciesApiFactory _factory;

    public RetrieveCurrentTermTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Today_inside_the_first_of_two_terms_shows_the_first_term()
    {
        var policy = await SeedTwoPaidTermsAndReadAt("GET1-TERM1-TODAY", TestClock.Now);

        PolicyResponseAssert.ShowsCurrentTerm(policy, 1, Term1Start, Term1End, 486.20m);
        Assert.Equal("Active", policy.Member("status").GetString());
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Last_day_of_the_first_term_still_shows_the_first_term()
    {
        var policy = await SeedTwoPaidTermsAndReadAt("GET1-TERM1-LAST", new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero));

        PolicyResponseAssert.ShowsCurrentTerm(policy, 1, Term1Start, Term1End, 486.20m);
        Assert.Equal("Active", policy.Member("status").GetString());
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task First_day_of_the_second_term_shows_the_second_term()
    {
        var policy = await SeedTwoPaidTermsAndReadAt("GET1-TERM2-FIRST", new DateTimeOffset(2027, 3, 2, 12, 0, 0, TimeSpan.Zero));

        PolicyResponseAssert.ShowsCurrentTerm(policy, 2, Term2Start, Term2End, 512.75m);
        Assert.Equal("Active", policy.Member("status").GetString());
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Day_after_the_latest_term_shows_the_latest_term_and_is_Expired()
    {
        var policy = await SeedTwoPaidTermsAndReadAt("GET1-AFTER-LATEST", new DateTimeOffset(2028, 3, 2, 12, 0, 0, TimeSpan.Zero));

        PolicyResponseAssert.ShowsCurrentTerm(policy, 2, Term2Start, Term2End, 512.75m);
        Assert.Equal("Expired", policy.Member("status").GetString());
    }

    // An unpaid term (a renewal without auto-renew) has a null paymentReference and no payment.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Unpaid_second_term_reads_back_with_no_payment_reference_and_no_payment()
    {
        const string reference = "GET1-UNPAID-T2";
        await _factory.Seed(new PolicyBuilder(reference)
            .WithAutoRenew(false)
            .WithTerms(
                new Term(Term1Start, 486.20m, new Payment("PAY-GET1-UNPAID-T1", PaymentMethod.Card, 486.20m)),
                new Term(Term2Start, 512.75m, null))
            .Build());
        using var client = _factory.CreateClient();

        using var read = await client.GetPolicy(reference);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var policy = await read.ReadJson();
        ReadsBackTheSeededPolicy(policy, reference, autoRenew: false);
        AssertTerms(policy, "PAY-GET1-UNPAID-T1", null);
        var payment = Assert.Single(policy.Member("payments").EnumerateArray());
        AssertPayment(payment, "PAY-GET1-UNPAID-T1", "Card", 486.20m);
        PolicyResponseAssert.ShowsCurrentTerm(policy, 1, Term1Start, Term1End, 486.20m);
        Assert.Equal("Active", policy.Member("status").GetString());
    }

    // Seeds both terms paid, moves the clock to readAt and reads the policy back whole: the stored values,
    // both terms in order and both payments, whatever the current term is.
    private async Task<JsonElement> SeedTwoPaidTermsAndReadAt(string reference, DateTimeOffset readAt)
    {
        var term1Payment = $"PAY-{reference}-1";
        var term2Payment = $"PAY-{reference}-2";
        await _factory.Seed(new PolicyBuilder(reference)
            .WithTerms(
                new Term(Term1Start, 486.20m, new Payment(term1Payment, PaymentMethod.Card, 486.20m)),
                new Term(Term2Start, 512.75m, new Payment(term2Payment, PaymentMethod.DirectDebit, 512.75m)))
            .Build());
        using var client = _factory.CreateClient();

        _factory.Clock.SetUtcNow(readAt);
        using var read = await client.GetPolicy(reference);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var policy = await read.ReadJson();
        ReadsBackTheSeededPolicy(policy, reference, autoRenew: true);
        AssertTerms(policy, term1Payment, term2Payment);
        var payments = policy.Member("payments").EnumerateArray().ToArray();
        Assert.Equal(2, payments.Length);
        AssertPayment(payments[0], term1Payment, "Card", 486.20m);
        AssertPayment(payments[1], term2Payment, "DirectDebit", 512.75m);
        return policy;
    }

    // The PolicyBuilder defaults as stored: BuyToLet, Mary Jackson born 1984-04-09, 31 Deansgate, Floor 4, M3 2BA.
    private static void ReadsBackTheSeededPolicy(JsonElement policy, string reference, bool autoRenew)
    {
        PolicyResponseAssert.HasTheContractShape(policy);
        Assert.Equal(reference, policy.Member("reference").GetString());
        Assert.Equal("BuyToLet", policy.Member("type").GetString());
        Assert.Equal(autoRenew, policy.Member("autoRenew").GetBoolean());

        var holder = Assert.Single(policy.Member("policyholders").EnumerateArray());
        Assert.Equal("Mary", holder.Member("firstName").GetString());
        Assert.Equal("Jackson", holder.Member("lastName").GetString());
        Assert.Equal(new DateOnly(1984, 4, 9), holder.GetDate("dateOfBirth"));

        var property = policy.Member("property");
        Assert.Equal("31 Deansgate", property.Member("addressLine1").GetString());
        Assert.Equal("Floor 4", property.Member("addressLine2").GetString());
        Assert.Equal(JsonValueKind.Null, property.Member("addressLine3").ValueKind);
        Assert.Equal("M3 2BA", property.Member("postcode").GetString());

        PolicyResponseAssert.HasNoClaimsRefundsOrCancellation(policy);
    }

    // terms[] is the whole history, oldest first, numbered from 1.
    private static void AssertTerms(JsonElement policy, string term1Payment, string? term2Payment)
    {
        var terms = policy.Member("terms").EnumerateArray().ToArray();
        Assert.Equal(2, terms.Length);
        AssertTerm(terms[0], 1, Term1Start, Term1End, 486.20m, term1Payment);
        AssertTerm(terms[1], 2, Term2Start, Term2End, 512.75m, term2Payment);
    }

    private static void AssertTerm(JsonElement term, int number, DateOnly start, DateOnly end, decimal amount, string? paymentReference)
    {
        Assert.Equal(number, term.Member("termNumber").GetInt32());
        Assert.Equal(start, term.GetDate("startDate"));
        Assert.Equal(end, term.GetDate("endDate"));
        Assert.Equal(amount, term.Member("amount").GetDecimal());
        Assert.Equal(paymentReference, term.Member("paymentReference").GetString());
    }

    private static void AssertPayment(JsonElement payment, string reference, string method, decimal amount)
    {
        Assert.Equal(reference, payment.Member("reference").GetString());
        Assert.Equal(method, payment.Member("type").GetString());
        Assert.Equal(amount, payment.Member("amount").GetDecimal());
    }
}
