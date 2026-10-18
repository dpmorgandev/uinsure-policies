using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-6: the request carries the payment method; the sale creates one Payment for the full policy
// amount with a generated reference, and term 1 links to it.
public sealed class SellPaymentTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellPaymentTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Sale_creates_one_payment_for_the_full_amount_by_the_request_method_with_a_generated_reference()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL6-PAYMENT").WithAmount(389.50m).WithPaymentMethod("DirectDebit").Build();

        var policy = await client.SellAndReadBack(body, "SELL6-PAYMENT");

        var payment = Assert.Single(policy.GetProperty("payments").EnumerateArray());
        Assert.Equal("DirectDebit", payment.GetProperty("type").GetString());
        Assert.Equal(389.50m, payment.GetProperty("amount").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(payment.GetProperty("reference").GetString()));
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Term_1_links_to_the_sale_payment_by_its_reference()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL6-TERM-LINK").Build();

        var policy = await client.SellAndReadBack(body, "SELL6-TERM-LINK");

        var payment = Assert.Single(policy.GetProperty("payments").EnumerateArray());
        var term = Assert.Single(policy.GetProperty("terms").EnumerateArray());
        Assert.Equal(payment.GetProperty("reference").GetString(), term.GetProperty("paymentReference").GetString());
    }

    // Each payment method is an equivalent input to the same behaviour.
    [Theory]
    [Trait("Rule", "SELL-6")]
    [InlineData("Card", "SELL6-CARD")]
    [InlineData("DirectDebit", "SELL6-DIRECT-DEBIT")]
    [InlineData("Cheque", "SELL6-CHEQUE")]
    public async Task Payment_is_recorded_with_the_method_the_request_carries(string method, string reference)
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder(reference).WithPaymentMethod(method).Build();

        var policy = await client.SellAndReadBack(body, reference);

        var payment = Assert.Single(policy.GetProperty("payments").EnumerateArray());
        Assert.Equal(method, payment.GetProperty("type").GetString());
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Two_sales_get_different_payment_references()
    {
        using var client = _factory.CreateClient();

        var first = await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL6-REF-ONE").Build(), "SELL6-REF-ONE");
        var second = await client.SellAndReadBack(new SellPolicyRequestBuilder("SELL6-REF-TWO").Build(), "SELL6-REF-TWO");

        var firstReference = Assert.Single(first.GetProperty("payments").EnumerateArray()).GetProperty("reference").GetString();
        var secondReference = Assert.Single(second.GetProperty("payments").EnumerateArray()).GetProperty("reference").GetString();
        Assert.NotEqual(firstReference, secondReference);
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Sale_without_a_payment_method_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL6-NO-METHOD").Build();
        body.Remove("paymentMethod");

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "paymentMethod");
        await client.AssertNotStored("SELL6-NO-METHOD");
    }
}
