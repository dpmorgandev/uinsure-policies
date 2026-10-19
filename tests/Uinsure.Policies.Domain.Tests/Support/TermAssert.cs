using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Domain.Tests.Support;

// A Term, member for member. Every expected value is the test's own literal, never derived here: Term is a class,
// so it has no value equality of its own. Payment is a record, so it is compared whole (reference, method, amount).
public static class TermAssert
{
    public static void Is(Term actual, DateOnly start, DateOnly end, decimal amount, Payment? payment)
    {
        Assert.Equal(start, actual.Start);
        Assert.Equal(end, actual.End);
        Assert.Equal(amount, actual.Amount);
        Assert.Equal(payment, actual.Payment);
    }
}
