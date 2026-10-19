using System.Text.Json;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// The E5 ClaimResponse, member for member. Returns the claim's generated reference, for the read-back to match.
internal static class ClaimAssert
{
    private static readonly string[] ClaimMembers = ["reference", "policyReference", "claimDate", "description", "hasClaims"];

    public static string IsClaim(JsonElement body, string policyReference, DateOnly claimDate, string? description)
    {
        PolicyResponseAssert.HasMembers(body, ClaimMembers, "claim");
        var reference = body.Member("reference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reference));
        Assert.NotEqual(policyReference, reference);
        Assert.Equal(policyReference, body.Member("policyReference").GetString());
        Assert.Equal(claimDate, body.GetDate("claimDate"));
        Assert.Equal(description, body.Member("description").GetString());

        // COULD-2: any claim sets the policy's hasClaims.
        Assert.True(body.Member("hasClaims").GetBoolean());
        return reference;
    }
}
