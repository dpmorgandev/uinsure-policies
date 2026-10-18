using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Support;

namespace Uinsure.Policies.Services.Tests.Builders;

// The SellPolicy command. Build() with no overrides is a valid sale on TestClock.Today: start 2026-11-08
// (today + 22), amount 527.18, one adult policyholder, paid by direct debit. Each test passes its own
// literal reference and overrides only the field under test. The reference is built directly, so a
// service test never depends on PolicyReference.Normalise.
public sealed class SellPolicyBuilder(string reference)
{
    private DateOnly _startDate = TestClock.Today.AddDays(22);
    private decimal _amount = 527.18m;
    private PaymentMethod _paymentMethod = PaymentMethod.DirectDebit;

    public SellPolicyBuilder WithStartDate(DateOnly value)
    {
        _startDate = value;
        return this;
    }

    public SellPolicyBuilder WithAmount(decimal value)
    {
        _amount = value;
        return this;
    }

    public SellPolicyBuilder WithPaymentMethod(PaymentMethod value)
    {
        _paymentMethod = value;
        return this;
    }

    public SellPolicy Build() => new(
        new PolicyReference(reference),
        PolicyType.Household,
        _startDate,
        _amount,
        true,
        [new Policyholder("Katherine", "Johnson", new DateOnly(1981, 8, 26))],
        new Property("48 Quay St", "Unit 5", null, "M3 4LZ"),
        _paymentMethod);
}
