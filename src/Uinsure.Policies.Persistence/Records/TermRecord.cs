namespace Uinsure.Policies.Persistence.Records;

internal sealed class TermRecord
{
    public int Id { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public decimal Amount { get; set; }

    // A term without a payment is unpaid.
    public PaymentRecord? Payment { get; set; }
}
