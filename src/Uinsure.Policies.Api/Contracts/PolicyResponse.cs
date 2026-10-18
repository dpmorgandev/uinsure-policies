namespace Uinsure.Policies.Api.Contracts;

// The bare shape F1 needs; F2 adds status, terms, payments and the rest of the contract.
public sealed record PolicyResponse(
    string Reference,
    PolicyType Type,
    DateOnly StartDate,
    decimal Amount,
    bool AutoRenew,
    PropertyDto Property);
