using Uinsure.Policies.Api.Tests.Support;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Api.Tests.Builders;

// A stored policy in a state no endpoint can make yet: more than one term, before F4's renewals exist. It is
// built with Policy.Restore, as Persistence rebuilds a stored policy (no rule runs), and seeded through the
// app's own IPolicyRepository (PolicySeeder); the test then reads it over HTTP. Build() with no overrides is
// one paid term that is in force on TestClock.Today, with no claims. None of it is the api.md example (Ada Lovelace,
// 1 High St, M1 1AA, 365.00, 2026-11-01).
public sealed class PolicyBuilder(string reference)
{
    private PolicyType _type = PolicyType.BuyToLet;
    private bool _autoRenew = true;
    private bool _hasClaims;
    private Policyholder[] _policyholders = [new("Mary", "Jackson", new DateOnly(1984, 4, 9))];
    private Property _property = new("31 Deansgate", "Floor 4", null, "M3 2BA");
    private Term[] _terms = [new(TestClock.Today.AddDays(-40), 446.50m, new Payment($"PAY-{reference}-1", PaymentMethod.Card, 446.50m))];

    public PolicyBuilder WithType(PolicyType value)
    {
        _type = value;
        return this;
    }

    public PolicyBuilder WithAutoRenew(bool value)
    {
        _autoRenew = value;
        return this;
    }

    // A stored claim flag: no endpoint can set it before F3's claims (COULD-2 makes it false at sale).
    public PolicyBuilder WithHasClaims(bool value)
    {
        _hasClaims = value;
        return this;
    }

    public PolicyBuilder WithPolicyholders(params Policyholder[] value)
    {
        _policyholders = value;
        return this;
    }

    public PolicyBuilder WithProperty(Property value)
    {
        _property = value;
        return this;
    }

    // The terms in order, oldest first, as a renewal appends them.
    public PolicyBuilder WithTerms(params Term[] value)
    {
        _terms = value;
        return this;
    }

    public Policy Build() => Policy.Restore(
        new PolicyReference(reference),
        _type,
        _autoRenew,
        _hasClaims,
        _policyholders,
        _property,
        _terms);
}
