namespace Uinsure.Policies.Services.Tests.Support;

// The fixed instant a service test starts from, the same one the integration tests use. A UK-boundary
// test builds its own instant instead. Boundaries are arithmetic from Today.
public static class TestClock
{
    public static DateTimeOffset Now { get; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    public static DateOnly Today { get; } = new(2026, 10, 17);

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
