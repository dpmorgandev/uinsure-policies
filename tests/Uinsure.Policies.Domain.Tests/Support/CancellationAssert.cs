using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Xunit;

namespace Uinsure.Policies.Domain.Tests.Support;

// A Cancellation, member for member. Every expected value is the test's own literal, never derived here.
// The record's own equality can't be used: it compares the two lists by reference.
public static class CancellationAssert
{
    public static void Is(
        Cancellation actual,
        DateOnly cancellationDate,
        string ruleApplied,
        decimal totalRefund,
        RefundCalculation[] terms,
        Refund[] refunds)
    {
        Assert.Equal(cancellationDate, actual.CancellationDate);
        Assert.Equal(ruleApplied, actual.RuleApplied);
        Assert.Equal(totalRefund, actual.TotalRefund);
        Assert.Equal(terms, actual.Terms);
        Assert.Equal(refunds, actual.Refunds);
    }
}
