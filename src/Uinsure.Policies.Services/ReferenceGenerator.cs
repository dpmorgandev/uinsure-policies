namespace Uinsure.Policies.Services;

public sealed class ReferenceGenerator : IReferenceGenerator
{
    public string Payment() => $"PAY-{Guid.NewGuid():N}";

    public string Refund() => throw new NotImplementedException();
}
