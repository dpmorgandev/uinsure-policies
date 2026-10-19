namespace Uinsure.Policies.Domain.Policies;

public sealed record Refund(string Reference, PaymentMethod Method, decimal Amount, string PaymentReference);
