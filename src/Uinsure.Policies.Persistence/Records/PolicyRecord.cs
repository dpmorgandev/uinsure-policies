using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Records;

internal sealed class PolicyRecord
{
    public int Id { get; set; }
    public required string Reference { get; set; }
    public PolicyType Type { get; set; }
    public bool AutoRenew { get; set; }
    public bool HasClaims { get; set; }

    // The concurrency token: the repository sets a new value on every save of a changed policy.
    public Guid Version { get; set; }

    public required List<PolicyholderRecord> Policyholders { get; set; }
    public required PropertyRecord Property { get; set; }
    public required List<TermRecord> Terms { get; set; }
}
