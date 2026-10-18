namespace Uinsure.Policies.Domain.Policies;

public sealed record Payment(string Reference, PaymentMethod Method, decimal Amount);
