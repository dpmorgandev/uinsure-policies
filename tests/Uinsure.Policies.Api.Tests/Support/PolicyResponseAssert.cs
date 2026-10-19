using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// The full PolicyResponse of the API contract (GET-1), read back. It extends the F1 read-back
// (SoldPolicyAssert) with the members F2 adds: status, currentTermNumber, hasClaims, refunds, claims and
// cancellation, and it pins the member set of every object, so a field missing, renamed or extra fails.
// Status and the end date are the test's own literals, never derived here.
internal static class PolicyResponseAssert
{
    private static readonly string[] PolicyMembers =
    [
        "reference", "type", "status", "currentTermNumber", "startDate", "endDate", "amount", "hasClaims",
        "autoRenew", "policyholders", "property", "terms", "payments", "refunds", "claims", "cancellation",
    ];

    private static readonly string[] PolicyholderMembers = ["firstName", "lastName", "dateOfBirth"];
    private static readonly string[] PropertyMembers = ["addressLine1", "addressLine2", "addressLine3", "postcode"];
    private static readonly string[] TermMembers = ["termNumber", "startDate", "endDate", "amount", "paymentReference"];
    private static readonly string[] PaymentMembers = ["reference", "type", "amount"];

    // A sold, one-term policy read back whole: every value sold, its term and payment, the derived status,
    // term 1 as the current term, no claims, no refunds and no cancellation.
    public static void ReadsBackAsSold(JsonElement policy, JsonObject sold, string expectedReference, DateOnly expectedEndDate, string expectedStatus)
    {
        HasTheContractShape(policy);
        SoldPolicyAssert.ReadsBackAsSold(policy, sold, expectedReference, expectedEndDate);
        Assert.Equal(expectedStatus, policy.Member("status").GetString());
        Assert.Equal(1, policy.Member("currentTermNumber").GetInt32());
        HasNoClaimsRefundsOrCancellation(policy);
    }

    // The policy-level view of the current term (GET-1): its number, dates and amount.
    public static void ShowsCurrentTerm(JsonElement policy, int termNumber, DateOnly start, DateOnly end, decimal amount)
    {
        Assert.Equal(termNumber, policy.Member("currentTermNumber").GetInt32());
        Assert.Equal(start, policy.GetDate("startDate"));
        Assert.Equal(end, policy.GetDate("endDate"));
        Assert.Equal(amount, policy.Member("amount").GetDecimal());
    }

    // A sold policy has no claim (hasClaims false at sale), no refund and no cancellation.
    public static void HasNoClaimsRefundsOrCancellation(JsonElement policy)
    {
        Assert.False(policy.Member("hasClaims").GetBoolean());
        Assert.Equal(JsonValueKind.Array, policy.Member("refunds").ValueKind);
        Assert.Empty(policy.Member("refunds").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, policy.Member("claims").ValueKind);
        Assert.Empty(policy.Member("claims").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, policy.Member("cancellation").ValueKind);
    }

    // Exactly the contract's members on the policy, each policyholder, the property, each term and each
    // payment. Refund, claim and cancellation members are F3's, asserted where those exist.
    public static void HasTheContractShape(JsonElement policy)
    {
        HasMembers(policy, PolicyMembers, "policy");
        HasMembers(policy.Member("property"), PropertyMembers, "property");
        foreach (var holder in policy.Member("policyholders").EnumerateArray())
        {
            HasMembers(holder, PolicyholderMembers, "policyholder");
        }

        foreach (var term in policy.Member("terms").EnumerateArray())
        {
            HasMembers(term, TermMembers, "term");
        }

        foreach (var payment in policy.Member("payments").EnumerateArray())
        {
            HasMembers(payment, PaymentMembers, "payment");
        }
    }

    // Exactly these members, no more and no fewer. Shared with the F3 asserts (CancellationAssert, PolicyStateAssert).
    internal static void HasMembers(JsonElement element, string[] expected, string what)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var actual = element.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            expected.Order(StringComparer.Ordinal).SequenceEqual(actual),
            $"The {what} members are [{string.Join(", ", actual)}]; the contract has [{string.Join(", ", expected)}].");
    }
}
