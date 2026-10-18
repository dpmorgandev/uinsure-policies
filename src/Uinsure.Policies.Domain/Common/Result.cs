namespace Uinsure.Policies.Domain.Common;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public required T Value { get; init; }
    public required IReadOnlyList<RuleViolation> Violations { get; init; }
    public static Result<T> Success(T value) => throw new NotImplementedException();
    public static Result<T> Failure(params RuleViolation[] violations) => throw new NotImplementedException();
}
