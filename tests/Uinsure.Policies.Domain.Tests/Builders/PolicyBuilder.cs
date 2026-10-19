using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests.Builders;

// The arguments of Policy.Sell. Sell() with no overrides is a valid sale on TestClock.Today: start
// 2026-11-08 (today + 22), amount 527.18, one adult policyholder. A test overrides only the field under
// test. The reference is built directly, so a sale test never depends on PolicyReference.Normalise.
public sealed class PolicyBuilder
{
    private PolicyReference _reference = new("DOM-0042");
    private PolicyType _type = PolicyType.Household;
    private DateOnly _startDate = TestClock.Today.AddDays(22);
    private decimal _amount = 527.18m;
    private bool _autoRenew = true;
    private PolicyholderBuilder[] _policyholders = [new PolicyholderBuilder()];
    private Property _property = new("48 Quay St", "Unit 5", null, "M3 4LZ");
    private PaymentMethod _paymentMethod = PaymentMethod.DirectDebit;
    private string _paymentReference = "PAY-DOM-0042";
    private DateOnly _today = TestClock.Today;

    public PolicyBuilder WithStartDate(DateOnly value)
    {
        _startDate = value;
        return this;
    }

    public PolicyBuilder WithAmount(decimal value)
    {
        _amount = value;
        return this;
    }

    public PolicyBuilder WithPolicyholders(params PolicyholderBuilder[] value)
    {
        _policyholders = value;
        return this;
    }

    public PolicyBuilder WithAutoRenew(bool value)
    {
        _autoRenew = value;
        return this;
    }

    public PolicyBuilder WithPaymentMethod(PaymentMethod value)
    {
        _paymentMethod = value;
        return this;
    }

    public PolicyBuilder WithPaymentReference(string value)
    {
        _paymentReference = value;
        return this;
    }

    public PolicyBuilder WithToday(DateOnly value)
    {
        _today = value;
        return this;
    }

    public Result<Policy> Sell() => Policy.Sell(
        _reference,
        _type,
        _startDate,
        _amount,
        _autoRenew,
        [.. _policyholders.Select(holder => holder.Build())],
        _property,
        _paymentMethod,
        _paymentReference,
        _today);

    public Policy Build()
    {
        var result = Sell();
        Assert.True(result.IsSuccess, "The builder's sale was expected to succeed.");
        return result.Value;
    }

    // A policy with the given terms, in order, rebuilt through Policy.Restore: no sale can make more than
    // one term until F4 adds renewal, so this is how a test gets a policy whose first and latest terms differ.
    public Policy RestoreWithTerms(params Term[] terms) => Policy.Restore(
        _reference,
        _type,
        _autoRenew,
        false,
        [.. _policyholders.Select(holder => holder.Build())],
        _property,
        terms,
        [],
        null);
}
