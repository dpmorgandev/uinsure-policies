namespace Uinsure.Policies.Services;

public enum ConflictReason
{
    Concurrency,
    DuplicateReference,
}

public sealed class PersistenceConflictException(ConflictReason reason) : Exception("The change conflicts with the stored data.")
{
    public ConflictReason Reason { get; } = reason;
}
