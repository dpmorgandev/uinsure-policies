using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Records;

internal sealed class PaymentRecord
{
    public int Id { get; set; }
    public required string Reference { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}
