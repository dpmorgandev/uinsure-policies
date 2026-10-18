using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Uinsure.Policies.Api.Contracts;

public enum PolicyType
{
    Household,
    BuyToLet,
}

public enum PaymentMethod
{
    Card,
    DirectDebit,
    Cheque,
}

// Required members are nullable so a missing field fails [Required] instead of binding as a default.
public sealed class SellPolicyRequest : IValidatableObject
{
    private static readonly Regex ReferencePattern = new("^[A-Z0-9-]{3,20}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private const decimal MaxAmount = 1_000_000.00m;

    [Required]
    public string? Reference { get; init; }

    [Required]
    public PolicyType? Type { get; init; }

    [Required]
    public DateOnly? StartDate { get; init; }

    [Required]
    public decimal? Amount { get; init; }

    [Required]
    public bool? AutoRenew { get; init; }

    [Required]
    public PaymentMethod? PaymentMethod { get; init; }

    [Required]
    public List<PolicyholderDto>? Policyholders { get; init; }

    [Required]
    public PropertyDto? Property { get; init; }

    // SELL-1: the pattern applies to the trimmed, upper-cased reference, so " abc-1 " is valid.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Reference is not null && !ReferencePattern.IsMatch(Reference.Trim().ToUpperInvariant()))
        {
            yield return new ValidationResult(
                "The reference must be 3 to 20 characters: letters, digits or hyphens.",
                [nameof(Reference)]);
        }

        // Shape, not SELL-7: the value must be a whole number of pence and at most 1,000,000.00. A zero or
        // negative amount within that scale is the 422 SELL-7. The remainder test cannot overflow for any
        // decimal, however negative, which a multiplication by 100 would.
        if (Amount is { } amount && (amount > MaxAmount || amount % 0.01m != 0))
        {
            yield return new ValidationResult(
                "The amount must be at most 1,000,000.00 with no more than 2 decimal places.",
                [nameof(Amount)]);
        }
    }
}

public sealed class PolicyholderDto
{
    [Required]
    [MaxLength(100)]
    public string? FirstName { get; init; }

    [Required]
    [MaxLength(100)]
    public string? LastName { get; init; }

    [Required]
    public DateOnly? DateOfBirth { get; init; }
}

public sealed class PropertyDto
{
    [Required]
    [MaxLength(200)]
    public string? AddressLine1 { get; init; }

    [MaxLength(200)]
    public string? AddressLine2 { get; init; }

    [MaxLength(200)]
    public string? AddressLine3 { get; init; }

    [Required]
    [MaxLength(8)]
    public string? Postcode { get; init; }
}
