namespace Uinsure.Policies.Services.Tests.Support;

// A clock fixed at one instant that counts how often the current time is read.
internal sealed class CountingTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public int UtcNowReads { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        UtcNowReads++;
        return utcNow;
    }
}
