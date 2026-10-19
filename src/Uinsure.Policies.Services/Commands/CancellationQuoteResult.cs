using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Services.Commands;

// QuotedOn is the UK today the quote was checked against, so the response cannot disagree with the check.
public sealed record CancellationQuoteResult(CancellationQuote Quote, DateOnly QuotedOn);
