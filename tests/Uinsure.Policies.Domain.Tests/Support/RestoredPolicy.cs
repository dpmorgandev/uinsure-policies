using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;

namespace Uinsure.Policies.Domain.Tests.Support;

// Policies no sale can make on TestClock.Today (2026-10-17): terms that started in the past, or more than one
// term. Each is rebuilt through Policy.Restore, which runs no rule.
public static class RestoredPolicy
{
    // 2026-03-01 to 2027-02-28 (365 days, 365.00 by Card, PAY-DOM-0042), with today 2026-10-17 in it, then the
    // renewal 2027-03-01 to 2028-02-29 (366 days, 512.40 by DirectDebit, PAY-DOM-0042-R1).
    public static Policy CardThenDirectDebit() => new PolicyBuilder().RestoreWithTerms(
        new Term(new DateOnly(2026, 3, 1), 365.00m, new Payment("PAY-DOM-0042", PaymentMethod.Card, 365.00m)),
        new Term(new DateOnly(2027, 3, 1), 512.40m, new Payment("PAY-DOM-0042-R1", PaymentMethod.DirectDebit, 512.40m)));

    // One paid term per start, in order, each 498.33 by DirectDebit.
    public static Policy WithTermsStarting(params DateOnly[] starts) => new PolicyBuilder().RestoreWithTerms(
        [.. starts.Select(start => new Term(start, 498.33m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 498.33m)))]);
}
