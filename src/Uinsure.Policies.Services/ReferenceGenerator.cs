namespace Uinsure.Policies.Services;

public sealed class ReferenceGenerator : IReferenceGenerator
{
    public string Payment() => $"PAY-{Guid.NewGuid():N}";

    public string Refund() => $"REF-{Guid.NewGuid():N}";
}
