namespace Uinsure.Policies.Domain.Tests.Support;

// The refund reference source a test hands to Policy.Cancel: REF-DOM-0001, REF-DOM-0002, … in call order, so
// each refund's reference is a literal the test can expect. No random data.
public sealed class RefundReferences
{
    private int _issued;

    public string Next()
    {
        _issued++;
        return $"REF-DOM-{_issued:0000}";
    }
}
