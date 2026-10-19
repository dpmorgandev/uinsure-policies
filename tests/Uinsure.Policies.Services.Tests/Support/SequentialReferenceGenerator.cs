namespace Uinsure.Policies.Services.Tests.Support;

// A reference generator with predictable output: PAY-SVC-1, PAY-SVC-2, … and REF-SVC-1, REF-SVC-2, …
// Each call is counted, so a test can see which references the service asked the seam for.
internal sealed class SequentialReferenceGenerator : IReferenceGenerator
{
    public int PaymentCalls { get; private set; }

    public int RefundCalls { get; private set; }

    public string Payment() => $"PAY-SVC-{++PaymentCalls}";

    public string Refund() => $"REF-SVC-{++RefundCalls}";
}
