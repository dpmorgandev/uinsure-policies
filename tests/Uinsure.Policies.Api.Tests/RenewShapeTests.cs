using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// E6, the shape of the renewal body. amount is required, a JSON number with at most 2 decimal places and at most
// 1,000,000.00 (H6); paymentMethod, when sent, is one of the PaymentMethod names, never an integer; no other
// member exists (a renewal cannot choose its own start date: RENEW-4). Each failure is a 400
// ValidationProblemDetails with no ruleId, returned before any rule, at /policies/{reference}/renewals, and the
// policy reads back as sold. Each policy is the builder's default sale:
//   start 2026-11-08 ends 2027-11-07, Pending on 2026-10-17.
public sealed class RenewShapeTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly DefaultEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public RenewShapeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_without_an_amount_is_rejected_with_400()
    {
        await AssertShape("RENEW-SHAPE-NO-AMT", new RenewalRequestBuilder().WithoutAmount().Build().ToJsonString(), "amount");
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_with_a_null_amount_is_rejected_with_400()
    {
        var body = new RenewalRequestBuilder().Build();
        body["amount"] = null;

        await AssertShape("RENEW-SHAPE-NULL-AMT", body.ToJsonString(), "amount");
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_with_3_decimal_places_is_rejected_with_400()
    {
        await AssertShape("RENEW-SHAPE-3DP", new RenewalRequestBuilder().WithAmount(432.105m).Build().ToJsonString(), "amount");
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_of_1_000_000_01_is_rejected_with_400()
    {
        await AssertShape("RENEW-SHAPE-1M-01", new RenewalRequestBuilder().WithAmount(1_000_000.01m).Build().ToJsonString(), "amount");
    }

    [Fact]
    [Trait("Rule", "RENEW-5")]
    public async Task Renewal_amount_sent_as_a_string_is_rejected_with_400()
    {
        var body = new RenewalRequestBuilder().Build();
        body["amount"] = "603.27";

        await AssertShape("RENEW-SHAPE-AMT-TEXT", body.ToJsonString(), "$.amount");
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_with_an_unknown_payment_method_is_rejected_with_400()
    {
        await AssertShape("RENEW-SHAPE-METHOD", new RenewalRequestBuilder().WithPaymentMethod("PayPal").Build().ToJsonString(), "$.paymentMethod");
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_with_an_integer_payment_method_is_rejected_with_400()
    {
        var body = new RenewalRequestBuilder().Build();
        body["paymentMethod"] = 1;

        await AssertShape("RENEW-SHAPE-INT-METH", body.ToJsonString(), "$.paymentMethod");
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewal_with_a_startDate_is_rejected_with_400_as_an_unknown_field()
    {
        var body = new RenewalRequestBuilder().Build();
        body["startDate"] = "2027-11-08";

        await AssertShape("RENEW-SHAPE-UNKNOWN", body.ToJsonString(), "$.startDate");
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewal_with_malformed_JSON_is_rejected_with_400()
    {
        await AssertShape("RENEW-SHAPE-BAD-JSON", "{\"amount\": 603.27", errorKey: null);
    }

    [Fact]
    [Trait("Rule", "RENEW-4")]
    public async Task Renewal_with_no_body_is_rejected_with_400()
    {
        const string reference = "RENEW-SHAPE-NO-BODY";
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostRenewalWithNoBody(reference);

        await ProblemAssert.ValidationErrors(response, RenewalActions.RenewalsPath(reference));
        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, DefaultEnd, "Pending");
    }

    // errorKey null: malformed JSON, whose errors entry the JSON reader names.
    private async Task AssertShape(string reference, string json, string? errorKey)
    {
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostRenewal(reference, json);

        var errors = await ProblemAssert.ValidationErrors(response, RenewalActions.RenewalsPath(reference));
        if (errorKey is not null)
        {
            ProblemAssert.HasError(errors, errorKey);
        }

        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, DefaultEnd, "Pending");
    }
}
