using Riok.Mapperly.Abstractions;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Rules;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence;

// Compile-time mapping between the Domain model and the stored records. Restore is hand-written because it
// must run no rule, and Version is not mapped because it is Persistence's own, set by PolicyRepository.
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
            [.. record.Terms.Select(ToDomain)],
            [.. record.Claims.Select(ToDomain)],
            record.Cancellation is null ? null : ToDomain(record.Cancellation));

    private static string ToValue(PolicyReference reference) => reference.Value;

    [MapperIgnoreTarget(nameof(PolicyholderRecord.Id))]
    private partial PolicyholderRecord ToRecord(Policyholder policyholder);

    [MapperIgnoreTarget(nameof(PropertyRecord.Id))]
    private partial PropertyRecord ToRecord(Property property);

    [MapperIgnoreTarget(nameof(TermRecord.Id))]
    private partial TermRecord ToRecord(Term term);

    [MapperIgnoreTarget(nameof(PaymentRecord.Id))]
    private partial PaymentRecord ToRecord(Payment payment);

    [MapperIgnoreTarget(nameof(ClaimRecord.Id))]
    public partial ClaimRecord ToRecord(Claim claim);

    [MapperIgnoreTarget(nameof(CancellationRecord.Id))]
    public partial CancellationRecord ToRecord(Cancellation cancellation);

    [MapperIgnoreTarget(nameof(RefundCalculationRecord.Id))]
    private partial RefundCalculationRecord ToRecord(RefundCalculation calculation);

    [MapperIgnoreTarget(nameof(RefundRecord.Id))]
    private partial RefundRecord ToRecord(Refund refund);

    private partial Claim ToDomain(ClaimRecord record);

    private partial Cancellation ToDomain(CancellationRecord record);

    private partial RefundCalculation ToDomain(RefundCalculationRecord record);

    private partial Refund ToDomain(RefundRecord record);

    private partial Policyholder ToDomain(PolicyholderRecord record);

    private partial Property ToDomain(PropertyRecord record);

    private partial Term ToDomain(TermRecord record);

    private partial Payment ToDomain(PaymentRecord record);
}
