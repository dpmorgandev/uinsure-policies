using System.Text.Json.Nodes;

namespace Uinsure.Policies.Api.Tests.Support;

// A policy seeded with the PolicyBuilder defaults (BuyToLet, auto-renew, Mary Jackson born 1984-04-09,
// 31 Deansgate, Floor 4, M3 2BA) and one paid term, written as the sale it stands for, so that SoldPolicyAssert
// can compare its read-back value for value.
internal static class SeededSale
{
    public static JsonObject For(string reference, DateOnly start, decimal amount, string paymentMethod) => new()
    {
        ["reference"] = reference,
        ["type"] = "BuyToLet",
        ["startDate"] = start.Iso(),
        ["amount"] = amount,
        ["autoRenew"] = true,
        ["paymentMethod"] = paymentMethod,
        ["policyholders"] = new JsonArray(new JsonObject { ["firstName"] = "Mary", ["lastName"] = "Jackson", ["dateOfBirth"] = "1984-04-09" }),
        ["property"] = new JsonObject { ["addressLine1"] = "31 Deansgate", ["addressLine2"] = "Floor 4", ["addressLine3"] = null, ["postcode"] = "M3 2BA" },
    };
}
