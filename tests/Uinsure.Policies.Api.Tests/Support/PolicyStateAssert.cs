using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// A stored refund as GET-1 lists it: its generated reference, the method and reference of the payment it
// reverses (CANCEL-2), and its amount as the exact JSON text.
internal sealed record ExpectedPolicyRefund(string Reference, string Type, string Amount, string PaymentReference);

// A stored claim as GET-1 lists it.
internal sealed record ExpectedClaim(string Reference, DateOnly ClaimDate, string? Description);

// The policy's cancellation summary as GET-1 shows it.
internal sealed record ExpectedCancellationSummary(DateOnly CancellationDate, string RuleApplied, string TotalRefund);

// The full read-back of a one-term policy after an F3 call (a cancellation, a claim, a quote or a rejected
// call): every value sold (SoldPolicyAssert), the contract's member set, the status, term 1 as the current
// term, and exactly the refunds, claims and cancellation the test expects, each member for member.
internal static class PolicyStateAssert
{
    private static readonly string[] RefundMembers = ["reference", "type", "amount", "paymentReference"];
    private static readonly string[] ClaimMembers = ["reference", "claimDate", "description"];
    private static readonly string[] CancellationMembers = ["cancellationDate", "ruleApplied", "totalRefund"];

    public static void ReadsBack(
        JsonElement policy,
        JsonObject sold,
        string reference,
        DateOnly expectedEnd,
        string expectedStatus,
        bool expectedHasClaims,
        ExpectedCancellationSummary? expectedCancellation,
        IReadOnlyList<ExpectedPolicyRefund> expectedRefunds,
        IReadOnlyList<ExpectedClaim> expectedClaims)
    {
        PolicyResponseAssert.HasTheContractShape(policy);
        SoldPolicyAssert.ReadsBackAsSold(policy, sold, reference, expectedEnd);
        Assert.Equal(expectedStatus, policy.Member("status").GetString());
        Assert.Equal(1, policy.Member("currentTermNumber").GetInt32());
        Assert.Equal(expectedHasClaims, policy.Member("hasClaims").GetBoolean());

        var refunds = policy.Member("refunds").EnumerateArray().ToArray();
        Assert.Equal(expectedRefunds.Count, refunds.Length);
        for (var i = 0; i < refunds.Length; i++)
        {
            var refund = refunds[i];
            var want = expectedRefunds[i];
            PolicyResponseAssert.HasMembers(refund, RefundMembers, "policy refund");
            Assert.Equal(want.Reference, refund.Member("reference").GetString());
            Assert.Equal(want.Type, refund.Member("type").GetString());
            CancellationAssert.Money(refund, "amount", want.Amount);
            Assert.Equal(want.PaymentReference, refund.Member("paymentReference").GetString());
        }

        var claims = policy.Member("claims").EnumerateArray().ToArray();
        Assert.Equal(expectedClaims.Count, claims.Length);
        for (var i = 0; i < claims.Length; i++)
        {
            var claim = claims[i];
            var want = expectedClaims[i];
            PolicyResponseAssert.HasMembers(claim, ClaimMembers, "policy claim");
            Assert.Equal(want.Reference, claim.Member("reference").GetString());
            Assert.Equal(want.ClaimDate, claim.GetDate("claimDate"));
            Assert.Equal(want.Description, claim.Member("description").GetString());
        }

        var cancellation = policy.Member("cancellation");
        if (expectedCancellation is null)
        {
            Assert.Equal(JsonValueKind.Null, cancellation.ValueKind);
            return;
        }

        PolicyResponseAssert.HasMembers(cancellation, CancellationMembers, "policy cancellation");
        Assert.Equal(expectedCancellation.CancellationDate, cancellation.GetDate("cancellationDate"));
        Assert.Equal(expectedCancellation.RuleApplied, cancellation.Member("ruleApplied").GetString());
        CancellationAssert.Money(cancellation, "totalRefund", expectedCancellation.TotalRefund);
    }

    // A call that was rejected, or a quote, left the policy exactly as sold: no claim, refund or cancellation.
    public static void ReadsBackUnchanged(JsonElement policy, JsonObject sold, string reference, DateOnly expectedEnd, string expectedStatus) =>
        ReadsBack(policy, sold, reference, expectedEnd, expectedStatus, false, null, [], []);
}
