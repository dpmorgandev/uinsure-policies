using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Commands;

// Number is the claim's place in the policy's list, taken when it was recorded.
// HasClaims is the policy's flag after the claim was recorded.
public sealed record RecordedClaim(Claim Claim, int Number, bool HasClaims);
