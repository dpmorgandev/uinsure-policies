namespace Uinsure.Policies.Domain.Common;

public sealed record RuleViolation(string RuleId, ViolationKind Kind, string Message, IReadOnlyDictionary<string, object?>? Extensions = null);
