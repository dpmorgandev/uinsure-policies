namespace Uinsure.Policies.Api.Tests.Support;

// The fixed instant every integration test starts from. Boundaries are arithmetic from Today.
public static class TestClock
{
    public static DateTimeOffset Now { get; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    public static DateOnly Today { get; } = new(2026, 10, 17);
}
