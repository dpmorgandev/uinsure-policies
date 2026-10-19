using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Services.Tests.Builders;

// A policy as the repository hands it back: rebuilt by Policy.Restore from stored terms, the way Persistence
// does, so a test can hold a policy with more than one term before Renew exists. Each term is the one term of
// a sale that starts on the given date (sold on that date, so the start window always passes), and so ends on
// start + 1 year - 1 day. Build() with no overrides is one term starting 2026-11-08, as a default sale does.
public sealed class StoredPolicyBuilder(string reference)
{
    private static readonly Policyholder Holder = new("Katherine", "Johnson", new DateOnly(1981, 8, 26));
    private static readonly Property Address = new("48 Quay St", "Unit 5", null, "M3 4LZ");

    private DateOnly[] _termStarts = [new DateOnly(2026, 11, 8)];

    public StoredPolicyBuilder WithTermsStarting(params DateOnly[] value)
    {
        _termStarts = value;
        return this;
    }

    public Policy Build() => Policy.Restore(
        new PolicyReference(reference),
        PolicyType.Household,
        true,
        false,
        [Holder],
        Address,
        [.. _termStarts.Select((start, index) => TermStarting(start, $"PAY-{reference}-{index + 1}"))],
        [],
        null);

    private static Term TermStarting(DateOnly start, string paymentReference)
    {
        var sale = Policy.Sell(
            new PolicyReference("TERM-SOURCE"),
            PolicyType.Household,
            start,
            527.18m,
            true,
            [Holder],
            Address,
            PaymentMethod.DirectDebit,
            paymentReference,
            start);
        Assert.True(sale.IsSuccess, "The sale a stored term is taken from was expected to succeed.");
        return Assert.Single(sale.Value.Terms);
    }
}
