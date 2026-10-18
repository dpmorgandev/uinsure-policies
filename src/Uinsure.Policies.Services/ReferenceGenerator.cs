namespace Uinsure.Policies.Services;

public sealed class ReferenceGenerator : IReferenceGenerator
{
    public static string Payment() => $"PAY-{Guid.NewGuid():N}";

    string IReferenceGenerator.Payment() => Payment();

    string IReferenceGenerator.Refund() => throw new NotImplementedException();
}
