using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Rules;

namespace Uinsure.Policies.Domain.Policies;

public sealed class Policy
{
    private const int MaxDaysAhead = 60;
    private const int MaxPolicyholders = 3;
    private const int MinimumAge = 16;

    // For EF Core to materialise a stored policy; the navigations are filled in after construction.
    private Policy()
    {
        Property = null!;
        Policyholders = new List<Policyholder>();
        Terms = new List<Term>();
    }

    private Policy(
        PolicyReference reference,
        PolicyType type,
        bool autoRenew,
        IReadOnlyList<Policyholder> policyholders,
        Property property,
        Term term)
    {
        Reference = reference;
        Type = type;
        AutoRenew = autoRenew;
        Policyholders = policyholders;
        Property = property;
        Terms = [term];
        Version = Guid.NewGuid();
    }

    public PolicyReference Reference { get; }
    public PolicyType Type { get; }
    public bool AutoRenew { get; }
    public bool HasClaims { get; }
    public Guid Version { get; }
    public IReadOnlyList<Policyholder> Policyholders { get; }
    public Property Property { get; }
    public IReadOnlyList<Term> Terms { get; }

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
        return Result<Policy>.Success(new Policy(reference, type, autoRenew, holders, property, term));
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

    private static RuleViolation Violation(string ruleId, string message, params (string Key, object? Value)[] extensions) =>
        new(ruleId, ViolationKind.BusinessRule, message, extensions.ToDictionary(e => e.Key, e => e.Value));
}
