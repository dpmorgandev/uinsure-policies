using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-6: PolicyService.Sell generates the payment reference ("PAY-…", the contract's form) and the
// sale's one Payment, for the full amount by the command's method, is what gets stored.
public sealed class PolicyServiceSellPaymentTests
{
    private readonly RecordingPolicyRepository _repository = new();
    private readonly PolicyService _service;

    public PolicyServiceSellPaymentTests()
    {
        _service = new PolicyService(_repository, new FakeTimeProvider(TestClock.Now));
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Sale_payment_gets_a_generated_PAY_reference()
    {
        var result = await Sell(new SellPolicyBuilder("SELL6-SVC-PAYREF").Build());

        var reference = SalePayment(result).Reference;
        Assert.StartsWith("PAY-", reference, StringComparison.Ordinal);
        Assert.True(reference.Length > "PAY-".Length, "Expected a generated part after 'PAY-'.");
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Two_sales_get_different_payment_references()
    {
        var first = await Sell(new SellPolicyBuilder("SELL6-SVC-ONE").Build());
        var second = await Sell(new SellPolicyBuilder("SELL6-SVC-TWO").Build());

        Assert.NotEqual(SalePayment(first).Reference, SalePayment(second).Reference);
    }

    // A non-default method and amount, so the payment can only come from this command.
    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Sale_payment_is_for_the_full_amount_by_the_command_method()
    {
        var result = await Sell(new SellPolicyBuilder("SELL6-SVC-FULL").WithPaymentMethod(PaymentMethod.Cheque).WithAmount(412.34m).Build());

        var payment = SalePayment(result);
        Assert.Equal(PaymentMethod.Cheque, payment.Method);
        Assert.Equal(412.34m, payment.Amount);
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public async Task Sale_is_added_once_and_saved_once_with_its_payment()
    {
        var result = await Sell(new SellPolicyBuilder("SELL6-SVC-SAVED").Build());

        var payment = SalePayment(result);
        Assert.Equal(1, _repository.AddCalls);
        Assert.Equal(1, _repository.SaveChangesCalls);
        var saved = Assert.Single(_repository.Saved);
        Assert.Same(result.Value.Policy, saved);
        Assert.Equal(payment, Assert.Single(saved.Terms).Payment);
    }

    private Task<Result<PolicyDetails>> Sell(SellPolicy command) =>
        _service.Sell(command, TestContext.Current.CancellationToken);

    private static Payment SalePayment(Result<PolicyDetails> result)
    {
        Assert.True(result.IsSuccess, "The sale was expected to succeed.");
        var term = Assert.Single(result.Value.Policy.Terms);
        Assert.NotNull(term.Payment);
        return term.Payment;
    }
}
