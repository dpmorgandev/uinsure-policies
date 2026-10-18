# Key type signatures

Binding signatures for the Domain and Services types. Part of the architecture skill.

```csharp
// Domain/Common
public enum ViolationKind { NotFound, Conflict, BusinessRule }        // → 404, 409, 422
public sealed record RuleViolation(string RuleId, ViolationKind Kind, string Message,
    IReadOnlyDictionary<string, object?>? Extensions = null);         // Extensions = rule-specific ProblemDetails members
public sealed class Result<T>
{
    public bool IsSuccess { get; }                                     // Violations.Count == 0
    public T Value { get; }                                            // throws InvalidOperationException on failure
    public IReadOnlyList<RuleViolation> Violations { get; }            // ordered by RuleIds.Order
    public static Result<T> Success(T value);
    public static Result<T> Failure(params RuleViolation[] violations); // ≥ 1
}
public static class Money { public static decimal Round(decimal amount); } // 2dp, AwayFromZero; the only rounding call (CANCEL-6)

// Domain/Rules
public static class RuleIds { public const string Sell1 = "SELL-1"; /* … every ID in requirements.md */
    public static IReadOnlyList<string> Order { get; } }               // requirements.md order; drives "first ruleId"
public sealed record RefundCalculation(decimal Amount, string RuleApplied, int TermDays, int UsedDays, int UnusedDays);
public static class RefundCalculator
{   // pure; RuleApplied ∈ CANCEL-1a | CANCEL-1b | CANCEL-1c | COULD-2 | UNPAID-TERM (term.Payment == null; wins over COULD-2; Amount 0); Money.Round called exactly once
    public static RefundCalculation ForTerm(Term term, DateOnly cancelOn, bool hasClaims);
}

// Domain/Policies
public readonly record struct PolicyReference(string Value)
{   public static PolicyReference Normalise(string raw); }             // Trim().ToUpperInvariant(); pattern checked at 400 level
public enum PolicyStatus { Pending, Active, Expired, Cancelled }
public sealed class Term
{
    public DateOnly Start { get; }  public DateOnly End { get; }  public decimal Amount { get; }
    public Payment? Payment { get; }                                   // null = unpaid (RENEW-3b)
    public int Days => End.DayNumber - Start.DayNumber + 1;            // 365 or 366
    public bool Covers(DateOnly date) => Start <= date && date <= End;
    public static DateOnly EndFor(DateOnly start) =>                   // SELL-3
        start is { Month: 2, Day: 29 }
            ? new DateOnly(start.Year + 1, 2, 28)                      // named test: 29 Feb 2028 → 28 Feb 2029
            : start.AddYears(1).AddDays(-1);                           // 1 Mar 2027 → 29 Feb 2028
}
public sealed class Policy
{
    public PolicyReference Reference { get; }  public PolicyType Type { get; }
    public bool AutoRenew { get; }  public bool HasClaims { get; }    // no Version: it lives on PolicyRecord (F1b)
    public IReadOnlyList<Policyholder> Policyholders { get; }  public Property Property { get; }
    public IReadOnlyList<Term> Terms { get; }  public IReadOnlyList<Claim> Claims { get; }
    public Cancellation? Cancellation { get; }
    public Term LatestTerm => Terms[^1];
    public Term CurrentTerm(DateOnly today);   // the term covering today, else LatestTerm
    public PolicyStatus StatusOn(DateOnly today);
    // Cancelled if Cancellation != null; Pending if today < Terms[0].Start; Expired if today > LatestTerm.End; else Active

    public static Policy Restore(PolicyReference reference, PolicyType type, bool autoRenew, bool hasClaims,
        IReadOnlyList<Policyholder> holders, Property property, IReadOnlyList<Term> terms,
        IReadOnlyList<Claim> claims, Cancellation? cancellation);     // persistence only (A13): stored state, no rules run
    public static Result<Policy> Sell(PolicyReference reference, PolicyType type, DateOnly start, decimal amount,
        bool autoRenew, IReadOnlyList<Policyholder> holders, Property property, PaymentMethod method,
        string paymentReference, DateOnly today);                      // SELL-2..7 (SELL-7: amount > 0)
    public Result<IReadOnlyList<RefundCalculation>> QuoteCancellation(DateOnly? on, DateOnly today); // COULD-1
    public Result<Cancellation> Cancel(DateOnly? on, DateOnly today, Func<string> refundReference);  // CANCEL-1..7, COULD-2
    public Result<Term> Renew(decimal amount, PaymentMethod? method, DateOnly today, Func<string> paymentReference); // RENEW-1..6, COULD-3, RENEW-3b
    public Result<Claim> RecordClaim(DateOnly claimDate, string? description, DateOnly today);         // COULD-2a..c
}

// Services
public interface IPolicyRepository
{
    Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct);
    void Add(Policy policy);
    Task SaveChanges(CancellationToken ct);   // EF exceptions → PersistenceConflictException
}
public enum ConflictReason { Concurrency, DuplicateReference }
public sealed class PersistenceConflictException(ConflictReason reason) : Exception;
public static class UkCalendar { public static DateOnly Today(TimeProvider clock); } // GetUtcNow() → Europe/London
public sealed record PolicyDetails(Policy Policy, Term CurrentTerm, PolicyStatus Status);
public interface IReferenceGenerator { string Payment(); string Refund(); }  // implemented by ReferenceGenerator
public interface IPolicyService { /* the methods below; PoliciesController depends on this */ }
public sealed class PolicyService(IPolicyRepository repository, IReferenceGenerator references, TimeProvider clock) : IPolicyService
{   // each method computes today once via UkCalendar.Today(clock)
    Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct);
    Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct);
    Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct);
    Task<Result<IReadOnlyList<RefundCalculation>>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct);
    Task<Result<Term>> Renew(PolicyReference reference, decimal amount, PaymentMethod? method, CancellationToken ct);
    Task<Result<Claim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);
}
```

