using System.ComponentModel.DataAnnotations;

namespace Uinsure.Policies.Api.Contracts;

// E6 body. amount is required; paymentMethod is optional (RENEW-3a defaults it, RENEW-3b forbids it for an unpaid term).
public sealed class RenewPolicyRequest : IValidatableObject
{
    private const decimal MaxAmount = 1_000_000.00m;

    [Required]
    public decimal? Amount { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    // Shape, not a rule: at most 1,000,000.00 and a whole number of pence.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount is { } amount && (amount > MaxAmount || amount % 0.01m != 0))
        {
            yield return new ValidationResult(
                "The amount must be at most 1,000,000.00 with no more than 2 decimal places.",
                [nameof(Amount)]);
        }
    }
}
