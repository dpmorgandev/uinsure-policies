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
        [RuleIds.Cancel3] = "Cancellation date in the past",
        [RuleIds.Cancel4] = "Policy already cancelled",
        [RuleIds.Cancel7] = "Cancellation date after the end of cover",
        [RuleIds.Could2a] = "Claim date in the future",
        [RuleIds.Could2b] = "Claim date outside cover",
        [RuleIds.Could2c] = "Claim on a cancelled policy",
        [RuleIds.Renew12] = "Outside the renewal window",
        [RuleIds.Renew3b] = "Payment method sent for an unpaid renewal",
        [RuleIds.Renew5] = "Renewal amount must be greater than zero",
        [RuleIds.Renew6] = "Cancelled policy cannot be renewed",
        [RuleIds.Could3] = "Cheque not allowed for auto-renewal",
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
