using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Commands;

public sealed record PolicyDetails(Policy Policy, Term Term);
