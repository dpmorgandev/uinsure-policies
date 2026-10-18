namespace Uinsure.Policies.Services;

public static class ReferenceGenerator
{
    public static string Payment() => $"PAY-{Guid.NewGuid():N}";
}
