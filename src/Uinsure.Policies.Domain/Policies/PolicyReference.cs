namespace Uinsure.Policies.Domain.Policies;

public readonly record struct PolicyReference(string Value)
{
    public static PolicyReference Normalise(string raw) => new(raw.Trim().ToUpperInvariant());
}
