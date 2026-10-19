namespace Uinsure.Policies.Persistence.Records;

internal sealed class CancellationRecord
{
    public int Id { get; set; }
    public DateOnly CancellationDate { get; set; }
    public required string RuleApplied { get; set; }
    public decimal TotalRefund { get; set; }

    // One calculation per term, in term order; a refund exists only for a calculation above zero.
    public required List<RefundCalculationRecord> Terms { get; set; }
    public required List<RefundRecord> Refunds { get; set; }
}
