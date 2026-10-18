using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Api.Errors;

// The one place a rule ID becomes an HTTP status, a problem type and a title.
internal static class ProblemCatalog
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        [RuleIds.Get1] = "Policy not found",
        [RuleIds.Sell1] = "Policy reference already exists",
        [RuleIds.Sell2] = "Start date outside the sale window",
        [RuleIds.Sell4] = "Wrong number of policyholders",
        [RuleIds.Sell5] = "Policyholder under 16",
        [RuleIds.Sell7] = "Policy amount must be greater than zero",
    };

    public static int StatusFor(ViolationKind kind) => kind switch
    {
        ViolationKind.NotFound => StatusCodes.Status404NotFound,
        ViolationKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    public static string TypeFor(string ruleId) => "/problems/" + ruleId.ToLowerInvariant().Replace('/', '-');

    public static string TitleFor(string ruleId) => Titles[ruleId];
}
