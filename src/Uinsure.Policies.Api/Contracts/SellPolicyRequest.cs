using System.ComponentModel.DataAnnotations;

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
public sealed class SellPolicyRequest
{
    [Required]
    [RegularExpression("^[A-Za-z0-9-]{3,20}$")]
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
}

public sealed class PolicyholderDto
{
    [Required]
    public string? FirstName { get; init; }

    [Required]
    public string? LastName { get; init; }

    [Required]
    public DateOnly? DateOfBirth { get; init; }
}

public sealed class PropertyDto
{
    [Required]
    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? AddressLine3 { get; init; }

    [Required]
    [MaxLength(8)]
    public string? Postcode { get; init; }
}
