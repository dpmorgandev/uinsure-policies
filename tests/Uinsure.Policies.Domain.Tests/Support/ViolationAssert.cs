using Uinsure.Policies.Domain.Common;
using Xunit;

namespace Uinsure.Policies.Domain.Tests.Support;

public static class ViolationAssert
{
    // A failed result carrying exactly one business-rule (422) violation of the given rule, with a message.
    public static RuleViolation SingleBusinessRule<T>(Result<T> result, string ruleId)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(ruleId, violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.False(string.IsNullOrWhiteSpace(violation.Message));
        return violation;
    }

    // A failed result carrying exactly one conflict (409) violation of the given rule, with a message: a 409
    // short-circuits, so no 422 rule is reported beside it.
    public static RuleViolation SingleConflict<T>(Result<T> result, string ruleId)
    {
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(ruleId, violation.RuleId);
        Assert.Equal(ViolationKind.Conflict, violation.Kind);
        Assert.False(string.IsNullOrWhiteSpace(violation.Message));
        return violation;
    }

    // A failed result carrying exactly these business-rule (422) violations, in this order, each with a message.
    public static void BusinessRules<T>(Result<T> result, params string[] ruleIds)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(ruleIds, result.Violations.Select(violation => violation.RuleId));
        Assert.All(result.Violations, violation =>
        {
            Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
            Assert.False(string.IsNullOrWhiteSpace(violation.Message));
        });
    }

    public static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
