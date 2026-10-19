using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// A term as GET-1 lists it: its dates, its amount as the exact JSON text, and the method of the payment it links
// to, or null for an unpaid term (RENEW-3b), whose paymentReference is null and which has no payment. Every value
// is the test's own literal, never derived here.
internal sealed record ExpectedPolicyTerm(DateOnly Start, DateOnly End, string Amount, string? PaymentType);

// The whole PolicyResponse of a policy with any number of terms: its status, which term is current, every term
// oldest first, and the cancellation and refunds where it was cancelled.
internal sealed record ExpectedRenewedPolicy(
    string Reference,
    string Status,
    int CurrentTermNumber,
    IReadOnlyList<ExpectedPolicyTerm> Terms,
    ExpectedCancellationSummary? Cancellation = null,
    IReadOnlyList<ExpectedPolicyRefund>? Refunds = null);

// The full read-back of a renewed policy (E6's body or E2), member for member: the contract's member set, every
// value sold (reference, type, autoRenew, policyholders, property), the status, the current term's number, dates
// and amount at the policy level, terms[] with each term's dates, amount and payment link, payments[] with one
// payment per paid term for that term's amount, and the refunds and cancellation. Money is compared as the raw
// JSON token, so 446.8 can't pass for 446.83 and an unrounded value shows as its own text.
internal static class RenewedPolicyAssert
{
    private static readonly string[] RefundMembers = ["reference", "type", "amount", "paymentReference"];
    private static readonly string[] CancellationMembers = ["cancellationDate", "ruleApplied", "totalRefund"];

    // knownPaymentReferences: the references read back earlier for the first terms, which must be unchanged.
    // Returns each term's payment reference (null for an unpaid term), in term order.
    public static IReadOnlyList<string?> ReadsBack(
        JsonElement policy,
        JsonObject sold,
        ExpectedRenewedPolicy expected,
        IReadOnlyList<string?> knownPaymentReferences)
    {
        PolicyResponseAssert.HasTheContractShape(policy);
        ReadsBackTheSoldDetails(policy, sold, expected.Reference);

        Assert.Equal(expected.Status, policy.Member("status").GetString());
        Assert.Equal(expected.CurrentTermNumber, policy.Member("currentTermNumber").GetInt32());
        var current = expected.Terms[expected.CurrentTermNumber - 1];
        Assert.Equal(current.Start, policy.GetDate("startDate"));
        Assert.Equal(current.End, policy.GetDate("endDate"));
        CancellationAssert.Money(policy, "amount", current.Amount);

        Assert.False(policy.Member("hasClaims").GetBoolean());
        Assert.Equal(JsonValueKind.Array, policy.Member("claims").ValueKind);
        Assert.Empty(policy.Member("claims").EnumerateArray());

        var paymentReferences = Terms(policy, expected.Terms);
        Payments(policy, expected.Terms, paymentReferences);

        Assert.True(knownPaymentReferences.Count <= paymentReferences.Count, "More payment references are known than there are terms.");
        for (var i = 0; i < knownPaymentReferences.Count; i++)
        {
            Assert.Equal(knownPaymentReferences[i], paymentReferences[i]);
        }

        Refunds(policy, expected.Refunds ?? []);
        Cancellation(policy, expected.Cancellation);
        return paymentReferences;
    }

    // The values the sale stored, which a renewal never changes.
    private static void ReadsBackTheSoldDetails(JsonElement policy, JsonObject sold, string reference)
    {
        Assert.Equal(reference, policy.Member("reference").GetString());
        Assert.Equal(sold["type"]!.GetValue<string>(), policy.Member("type").GetString());
        Assert.Equal(sold["autoRenew"]!.GetValue<bool>(), policy.Member("autoRenew").GetBoolean());

        var soldProperty = sold["property"]!.AsObject();
        var property = policy.Member("property");
        foreach (var line in (string[])["addressLine1", "addressLine2", "addressLine3", "postcode"])
        {
            Assert.Equal(soldProperty[line]?.GetValue<string>(), property.Member(line).GetString());
        }

        var soldHolders = sold["policyholders"]!.AsArray()
            .Select(node => node!.AsObject())
            .Select(holder => (
                holder["firstName"]?.GetValue<string>(),
                holder["lastName"]?.GetValue<string>(),
                DateOnly.ParseExact(holder["dateOfBirth"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToArray();
        var holders = policy.Member("policyholders").EnumerateArray()
            .Select(holder => (holder.Member("firstName").GetString(), holder.Member("lastName").GetString(), holder.GetDate("dateOfBirth")))
            .ToArray();
        Assert.Equal(soldHolders, holders);
    }

    // terms[] is the whole history, oldest first, numbered from 1, under the one reference (RENEW-4).
    private static List<string?> Terms(JsonElement policy, IReadOnlyList<ExpectedPolicyTerm> expected)
    {
        var terms = policy.Member("terms").EnumerateArray().ToArray();
        Assert.Equal(expected.Count, terms.Length);

        var paymentReferences = new List<string?>();
        for (var i = 0; i < terms.Length; i++)
        {
            var term = terms[i];
            var want = expected[i];
            Assert.Equal(i + 1, term.Member("termNumber").GetInt32());
            Assert.Equal(want.Start, term.GetDate("startDate"));
            Assert.Equal(want.End, term.GetDate("endDate"));
            CancellationAssert.Money(term, "amount", want.Amount);

            var link = term.Member("paymentReference");
            if (want.PaymentType is null)
            {
                Assert.Equal(JsonValueKind.Null, link.ValueKind);
                paymentReferences.Add(null);
            }
            else
            {
                var reference = link.GetString();
                Assert.False(string.IsNullOrWhiteSpace(reference), $"Term {i + 1} is paid but has no paymentReference.");
                paymentReferences.Add(reference);
            }
        }

        return paymentReferences;
    }

    // One payment per paid term, in term order, for that term's amount by its method (SELL-6, RENEW-3a), and no
    // payment for an unpaid term (RENEW-3b). Every payment has a reference of its own.
    private static void Payments(JsonElement policy, IReadOnlyList<ExpectedPolicyTerm> expected, List<string?> paymentReferences)
    {
        var paid = expected
            .Select((term, index) => (Term: term, Reference: paymentReferences[index]))
            .Where(pair => pair.Term.PaymentType is not null)
            .ToArray();
        var payments = policy.Member("payments").EnumerateArray().ToArray();
        Assert.Equal(paid.Length, payments.Length);

        for (var i = 0; i < payments.Length; i++)
        {
            var payment = payments[i];
            Assert.Equal(paid[i].Reference, payment.Member("reference").GetString());
            Assert.Equal(paid[i].Term.PaymentType, payment.Member("type").GetString());
            CancellationAssert.Money(payment, "amount", paid[i].Term.Amount);
        }

        Assert.Equal(paid.Length, paid.Select(pair => pair.Reference).Distinct(StringComparer.Ordinal).Count());
    }

    private static void Refunds(JsonElement policy, IReadOnlyList<ExpectedPolicyRefund> expected)
    {
        Assert.Equal(JsonValueKind.Array, policy.Member("refunds").ValueKind);
        var refunds = policy.Member("refunds").EnumerateArray().ToArray();
        Assert.Equal(expected.Count, refunds.Length);
        for (var i = 0; i < refunds.Length; i++)
        {
            var refund = refunds[i];
            var want = expected[i];
            PolicyResponseAssert.HasMembers(refund, RefundMembers, "policy refund");
            Assert.Equal(want.Reference, refund.Member("reference").GetString());
            Assert.Equal(want.Type, refund.Member("type").GetString());
            CancellationAssert.Money(refund, "amount", want.Amount);
            Assert.Equal(want.PaymentReference, refund.Member("paymentReference").GetString());
        }
    }

    private static void Cancellation(JsonElement policy, ExpectedCancellationSummary? expected)
    {
        var cancellation = policy.Member("cancellation");
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, cancellation.ValueKind);
            return;
        }

        PolicyResponseAssert.HasMembers(cancellation, CancellationMembers, "policy cancellation");
        Assert.Equal(expected.CancellationDate, cancellation.GetDate("cancellationDate"));
        Assert.Equal(expected.RuleApplied, cancellation.Member("ruleApplied").GetString());
        CancellationAssert.Money(cancellation, "totalRefund", expected.TotalRefund);
    }
}
