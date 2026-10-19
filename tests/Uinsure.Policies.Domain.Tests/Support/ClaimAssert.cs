using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Domain.Tests.Support;

// A recorded claim's date and description, member by member. Its other members (such as any reference) are not
// the domain rule's to fix, so the record's own equality isn't used.
public static class ClaimAssert
{
    public static void IsClaim(Result<Claim> result, DateOnly claimDate, string? description)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal(claimDate, result.Value.ClaimDate);
        Assert.Equal(description, result.Value.Description);
    }
}
