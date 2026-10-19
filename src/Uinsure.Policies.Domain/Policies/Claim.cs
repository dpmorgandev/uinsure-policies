namespace Uinsure.Policies.Domain.Policies;

public sealed record Claim(DateOnly ClaimDate, string? Description);
