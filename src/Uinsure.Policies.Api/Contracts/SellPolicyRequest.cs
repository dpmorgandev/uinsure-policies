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
