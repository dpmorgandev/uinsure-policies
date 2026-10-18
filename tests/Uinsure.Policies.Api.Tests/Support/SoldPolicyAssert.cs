using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// The bare F1 GET compared with the request that sold it, so only a stored sale can pass: an echoing or
// canned stub has no way to know the sold values. It reads only F1 fields (no status, currentTermNumber,
// hasClaims, refunds, claims or cancellation, which are F2's).
internal static class SoldPolicyAssert
{
    // expectedEndDate is the test's own literal (SELL-3), never derived here: a 29 Feb start ends on 28 Feb.
    public static void ReadsBackAsSold(JsonElement policy, JsonObject sold, string expectedReference, DateOnly expectedEndDate)
    {
        var startDate = SoldDate(sold, "startDate");
        var amount = sold["amount"]!.GetValue<decimal>();

        Assert.Equal(expectedReference, policy.Member("reference").GetString());
        Assert.Equal(sold["type"]!.GetValue<string>(), policy.Member("type").GetString());
        Assert.Equal(startDate, policy.GetDate("startDate"));
        Assert.Equal(amount, policy.Member("amount").GetDecimal());
        Assert.Equal(sold["autoRenew"]!.GetValue<bool>(), policy.Member("autoRenew").GetBoolean());

        var soldProperty = sold["property"]!.AsObject();
        var property = policy.Member("property");
        foreach (var line in (string[])["addressLine1", "addressLine2", "addressLine3", "postcode"])
        {
            Assert.Equal(soldProperty[line]?.GetValue<string>(), property.Member(line).GetString());
        }

        // Every policyholder, whole and in the order sold.
        var soldHolders = sold["policyholders"]!.AsArray()
            .Select(node => node!.AsObject())
            .Select(holder => (holder["firstName"]?.GetValue<string>(), holder["lastName"]?.GetValue<string>(), SoldDate(holder, "dateOfBirth")))
            .ToArray();
        var holders = policy.Member("policyholders").EnumerateArray()
            .Select(holder => (holder.Member("firstName").GetString(), holder.Member("lastName").GetString(), holder.GetDate("dateOfBirth")))
            .ToArray();
        Assert.Equal(soldHolders, holders);

        Assert.Equal(expectedEndDate, policy.GetDate("endDate"));

        // SELL-3: one term, numbered 1, for the sold dates and amount. SELL-6: one payment for the full
        // amount by the sold method, with a generated reference that term 1 links to.
        var term = Assert.Single(policy.Member("terms").EnumerateArray());
        Assert.Equal(1, term.Member("termNumber").GetInt32());
        Assert.Equal(startDate, term.GetDate("startDate"));
        Assert.Equal(expectedEndDate, term.GetDate("endDate"));
        Assert.Equal(amount, term.Member("amount").GetDecimal());

        var payment = Assert.Single(policy.Member("payments").EnumerateArray());
        Assert.Equal(sold["paymentMethod"]!.GetValue<string>(), payment.Member("type").GetString());
        Assert.Equal(amount, payment.Member("amount").GetDecimal());
        var paymentReference = payment.Member("reference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(paymentReference));
        Assert.Equal(paymentReference, term.Member("paymentReference").GetString());
    }

    private static DateOnly SoldDate(JsonObject sold, string property) =>
        DateOnly.ParseExact(sold[property]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
