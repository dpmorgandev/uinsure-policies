namespace Uinsure.Policies.Domain.Policies;

public sealed record PolicyReference(string Value)
{
    public static PolicyReference Normalise(string raw) => throw new NotImplementedException();
}
