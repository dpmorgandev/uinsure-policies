using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Domain.Common;

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, IReadOnlyList<RuleViolation> violations)
    {
        _value = value;
        Violations = violations;
    }

    public bool IsSuccess => Violations.Count == 0;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public IReadOnlyList<RuleViolation> Violations { get; }

    public static Result<T> Success(T value) => new(value, []);

    public static Result<T> Failure(params RuleViolation[] violations)
    {
        if (violations.Length == 0)
        {
            throw new ArgumentException("A failure needs at least one violation.", nameof(violations));
        }

        var order = RuleIds.Order.ToList();
        var ordered = violations.OrderBy(violation => order.IndexOf(violation.RuleId)).ToArray();
        return new Result<T>(default, ordered);
    }
}
