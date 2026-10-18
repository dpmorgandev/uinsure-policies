using Riok.Mapperly.Abstractions;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence;

// Compile-time mapping between the Domain model and the stored records. Version and Restore are hand-written
// because they are not field copies: Version is Persistence's own, and Restore must run no rule.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal sealed partial class PolicyRecordMapper
{
    [MapperIgnoreTarget(nameof(PolicyRecord.Id))]
    [MapperIgnoreTarget(nameof(PolicyRecord.Version))]
    public partial PolicyRecord ToRecord(Policy policy);

    public Policy Restore(PolicyRecord record) =>
        Policy.Restore(
            new PolicyReference(record.Reference),
            record.Type,
            record.AutoRenew,
            record.HasClaims,
            [.. record.Policyholders.Select(ToDomain)],
            ToDomain(record.Property),
            [.. record.Terms.Select(ToDomain)]);

    private static string ToValue(PolicyReference reference) => reference.Value;

    [MapperIgnoreTarget(nameof(PolicyholderRecord.Id))]
    private partial PolicyholderRecord ToRecord(Policyholder policyholder);

    [MapperIgnoreTarget(nameof(PropertyRecord.Id))]
    private partial PropertyRecord ToRecord(Property property);

    [MapperIgnoreTarget(nameof(TermRecord.Id))]
    private partial TermRecord ToRecord(Term term);

    [MapperIgnoreTarget(nameof(PaymentRecord.Id))]
    private partial PaymentRecord ToRecord(Payment payment);

    private partial Policyholder ToDomain(PolicyholderRecord record);

    private partial Property ToDomain(PropertyRecord record);

    private partial Term ToDomain(TermRecord record);

    private partial Payment ToDomain(PaymentRecord record);
}
