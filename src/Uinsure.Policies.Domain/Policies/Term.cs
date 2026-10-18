namespace Uinsure.Policies.Domain.Policies;

public sealed class Term
{
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public decimal Amount { get; }
    public Payment? Payment { get; }
    public static DateOnly EndFor(DateOnly start) => throw new NotImplementedException();
}
