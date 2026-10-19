namespace Uinsure.Policies.Domain.Common;

public static class Money
{
    // CANCEL-6: 2 dp, half away from zero. .NET's default is banker's rounding, so this is the one place it is chosen.
#pragma warning disable RS0030 // The only permitted call to decimal rounding (architecture test A11).
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
#pragma warning restore RS0030
}
