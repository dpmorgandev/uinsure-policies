namespace Uinsure.Policies.Persistence.Records;

internal sealed class RefundCalculationRecord
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public required string RuleApplied { get; set; }
    public int TermDays { get; set; }
    public int UsedDays { get; set; }
    public int UnusedDays { get; set; }
}
