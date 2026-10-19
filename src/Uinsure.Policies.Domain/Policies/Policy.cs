using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Domain.Policies;

public sealed class Policy
{
    private const int MaxDaysAhead = 60;
    private const int MaxPolicyholders = 3;
    private const int MinimumAge = 16;

    private readonly List<Claim> _claims;

    private Policy(
        PolicyReference reference,
        PolicyType type,
        bool autoRenew,
        bool hasClaims,
        IReadOnlyList<Policyholder> policyholders,
        Property property,
        IReadOnlyList<Term> terms,
        IReadOnlyList<Claim> claims,
        Cancellation? cancellation)
    {
        _claims = [.. claims];
        Cancellation = cancellation;
        Reference = reference;
        Type = type;
        AutoRenew = autoRenew;
        HasClaims = hasClaims;
        Policyholders = policyholders;
        Property = property;
        Terms = terms;
    }

    public PolicyReference Reference { get; }
    public PolicyType Type { get; }
    public bool AutoRenew { get; }
    public bool HasClaims { get; private set; }
    public IReadOnlyList<Policyholder> Policyholders { get; }
    public Property Property { get; }
    public IReadOnlyList<Term> Terms { get; }
    public IReadOnlyList<Claim> Claims => _claims;
    public Cancellation? Cancellation { get; private set; }

    public Term LatestTerm => Terms[^1];

    // The term covering today, else the latest (before the first start or after the last end).
    public Term CurrentTerm(DateOnly today) => Terms.FirstOrDefault(t => t.Covers(today)) ?? LatestTerm;

    // Derived, never stored.
    public PolicyStatus StatusOn(DateOnly today)
    {
        if (Cancellation is not null)
        {
            return PolicyStatus.Cancelled;
        }

        if (today < Terms[0].Start)
        {
            return PolicyStatus.Pending;
        }

        return today > LatestTerm.End ? PolicyStatus.Expired : PolicyStatus.Active;
    }

    // Persistence only: rebuilds stored state and runs no rule.
    public static Policy Restore(
        PolicyReference reference,
        PolicyType type,
        bool autoRenew,
        bool hasClaims,
        IReadOnlyList<Policyholder> holders,
        Property property,
        IReadOnlyList<Term> terms,
        IReadOnlyList<Claim>? claims = null,
        Cancellation? cancellation = null) =>
        new(reference, type, autoRenew, hasClaims, holders, property, terms, claims ?? [], cancellation);

    // COULD-1: the cancel calculation, read only.
    public Result<IReadOnlyList<RefundCalculation>> QuoteCancellation(DateOnly? on, DateOnly today)
    {
        var date = ValidateCancellationDate(on, today);
        return date.IsSuccess
            ? Result<IReadOnlyList<RefundCalculation>>.Success(Calculate(date.Value))
            : Result<IReadOnlyList<RefundCalculation>>.Failure([.. date.Violations]);
    }

    public Result<Cancellation> Cancel(DateOnly? on, DateOnly today, Func<string> refundReference)
    {
        var date = ValidateCancellationDate(on, today);
        if (!date.IsSuccess)
        {
            return Result<Cancellation>.Failure([.. date.Violations]);
        }

        var calculations = Calculate(date.Value);

        // No £0 refund: only a term with a payment and an amount above 0 is reversed (CANCEL-2).
        var refunds = new List<Refund>();
        for (var i = 0; i < Terms.Count; i++)
        {
            if (calculations[i].Amount > 0 && Terms[i].Payment is { } payment)
            {
                refunds.Add(new Refund(refundReference(), payment.Method, calculations[i].Amount, payment.Reference));
            }
        }

        // The top-level rule is the current term's.
        var currentIndex = Terms.ToList().IndexOf(CurrentTerm(date.Value));
        var cancellation = new Cancellation(
            date.Value, calculations[currentIndex].RuleApplied, calculations.Sum(c => c.Amount), calculations, refunds);
        Cancellation = cancellation;
        return Result<Cancellation>.Success(cancellation);
    }

    // COULD-2a/2b are both collected; COULD-2c is a conflict and is returned alone.
    public Result<Claim> RecordClaim(DateOnly claimDate, string? description, DateOnly today)
    {
        if (Cancellation is not null)
        {
            return Result<Claim>.Failure(Conflict(
                RuleIds.Could2c,
                $"The policy was cancelled on {Cancellation.CancellationDate:yyyy-MM-dd}, so a claim cannot be recorded.",
                ("cancellationDate", Cancellation.CancellationDate)));
        }

        var violations = new List<RuleViolation>();
        if (claimDate > today)
        {
            violations.Add(Violation(RuleIds.Could2a, "The claim date cannot be in the future.", ("today", today)));
        }

        if (!Terms.Any(term => term.Covers(claimDate)))
        {
            violations.Add(Violation(
                RuleIds.Could2b,
                $"The claim date must be within the cover, {Terms[0].Start:yyyy-MM-dd} to {LatestTerm.End:yyyy-MM-dd}.",
                ("coverStart", Terms[0].Start),
                ("coverEnd", LatestTerm.End)));
        }

        if (violations.Count > 0)
        {
            return Result<Claim>.Failure([.. violations]);
        }

        var claim = new Claim(claimDate, description);
        _claims.Add(claim);
        HasClaims = true;
        return Result<Claim>.Success(claim);
    }

    // CANCEL-4 is a conflict and is returned alone; CANCEL-3 and CANCEL-7 are both collected.
    private Result<DateOnly> ValidateCancellationDate(DateOnly? on, DateOnly today)
    {
        if (Cancellation is not null)
        {
            return Result<DateOnly>.Failure(Conflict(
                RuleIds.Cancel4,
                $"The policy was already cancelled on {Cancellation.CancellationDate:yyyy-MM-dd}.",
                ("cancellationDate", Cancellation.CancellationDate)));
        }

        var date = on ?? today;
        var violations = new List<RuleViolation>();
        if (date < today)
        {
            violations.Add(Violation(RuleIds.Cancel3, "The cancellation date cannot be in the past.", ("today", today)));
        }

        if (date > LatestTerm.End)
        {
            violations.Add(Violation(
                RuleIds.Cancel7,
                $"The cancellation date must be on or before the end of cover, {LatestTerm.End:yyyy-MM-dd}.",
                ("latestEndDate", LatestTerm.End)));
        }

        return violations.Count > 0
            ? Result<DateOnly>.Failure([.. violations])
            : Result<DateOnly>.Success(date);
    }

    private List<RefundCalculation> Calculate(DateOnly cancelOn) =>
        [.. Terms.Select(term => RefundCalculator.ForTerm(term, cancelOn, HasClaims))];

    // SELL-2, 4, 5 and 7 are all checked; none returns early.
    public static Result<Policy> Sell(
        PolicyReference reference,
        PolicyType type,
        DateOnly start,
        decimal amount,
        bool autoRenew,
        IReadOnlyList<Policyholder> holders,
        Property property,
        PaymentMethod method,
        string paymentReference,
        DateOnly today)
    {
        var violations = new List<RuleViolation>();

        var earliest = today;
        var latest = today.AddDays(MaxDaysAhead);
        if (start < earliest || start > latest)
        {
            violations.Add(Violation(
                RuleIds.Sell2,
                $"The start date must be between {earliest:yyyy-MM-dd} and {latest:yyyy-MM-dd}.",
                ("earliest", earliest),
                ("latest", latest)));
        }

        if (holders.Count is < 1 or > MaxPolicyholders)
        {
            violations.Add(Violation(
                RuleIds.Sell4,
                $"A policy needs 1 to {MaxPolicyholders} policyholders.",
                ("count", holders.Count)));
        }

        for (var i = 0; i < holders.Count; i++)
        {
            if (!IsOldEnough(holders[i].DateOfBirth, start))
            {
                violations.Add(Violation(
                    RuleIds.Sell5,
                    $"Policyholder {i + 1} must be {MinimumAge} or older on the start date.",
                    ("index", i + 1)));
            }
        }

        if (amount <= 0)
        {
            violations.Add(Violation(RuleIds.Sell7, "The policy amount must be greater than 0.", ("amount", amount)));
        }

        if (violations.Count > 0)
        {
            return Result<Policy>.Failure([.. violations]);
        }

        // SELL-6: the payment is for the full amount, by the method sold with.
        var payment = new Payment(paymentReference, method, amount);
        var term = new Term(start, amount, payment);
        return Result<Policy>.Success(new Policy(reference, type, autoRenew, false, holders, property, [term], [], null));
    }

    // SELL-5: 16 or older on the start date, so the 16th birthday itself passes. The years are compared first
    // so that no extreme dateOfBirth or startDate can make AddYears overflow; it only runs in the birthday year.
    private static bool IsOldEnough(DateOnly dateOfBirth, DateOnly start)
    {
        var birthdayYear = start.Year - MinimumAge;
        if (dateOfBirth.Year != birthdayYear)
        {
            return dateOfBirth.Year < birthdayYear;
        }

        return dateOfBirth.AddYears(MinimumAge) <= start;
    }

    private static RuleViolation Conflict(string ruleId, string message, params (string Key, object? Value)[] extensions) =>
        new(ruleId, ViolationKind.Conflict, message, extensions.ToDictionary(e => e.Key, e => e.Value));

    private static RuleViolation Violation(string ruleId, string message, params (string Key, object? Value)[] extensions) =>
        new(ruleId, ViolationKind.BusinessRule, message, extensions.ToDictionary(e => e.Key, e => e.Value));
}
