namespace Uinsure.Policies.Domain.Tests.Support;

// The UK date every domain test passes in as today, the same date the integration tests' clock gives.
// Boundaries are arithmetic from Today; the domain never sees a clock.
public static class TestClock
{
    public static DateOnly Today { get; } = new(2026, 10, 17);
}
