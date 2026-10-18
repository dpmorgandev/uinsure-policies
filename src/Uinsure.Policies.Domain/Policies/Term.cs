namespace Uinsure.Policies.Domain.Policies;

public sealed class Term
{
    // For EF Core to materialise a stored term.
    private Term()
    {
    }

    internal Term(DateOnly start, decimal amount, Payment? payment)
    {
        Start = start;
        End = EndFor(start);
        Amount = amount;
        Payment = payment;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }
    public decimal Amount { get; }
    public Payment? Payment { get; }

    public bool Covers(DateOnly date) => Start <= date && date <= End;

    // SELL-3: start + 1 year - 1 day; a 29 Feb start has no anniversary, so it ends on 28 Feb.
    public static DateOnly EndFor(DateOnly start) =>
        start is { Month: 2, Day: 29 }
            ? new DateOnly(start.Year + 1, 2, 28)
            : start.AddYears(1).AddDays(-1);
}
