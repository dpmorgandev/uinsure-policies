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

    public static object? Extension(RuleViolation violation, string key)
    {
        Assert.NotNull(violation.Extensions);
        Assert.True(violation.Extensions.ContainsKey(key), $"Expected the extension '{key}'.");
        return violation.Extensions[key];
    }
}
