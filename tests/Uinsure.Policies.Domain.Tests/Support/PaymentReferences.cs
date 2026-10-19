namespace Uinsure.Policies.Domain.Tests.Support;

// The payment reference source a test hands to Policy.Renew: PAY-DOM-R0001, PAY-DOM-R0002, … in call order, so
// each renewal payment's reference is a literal the test can expect. No random data.
public sealed class PaymentReferences
{
    private int _issued;

    public string Next()
    {
        _issued++;
        return $"PAY-DOM-R{_issued:0000}";
    }
}
