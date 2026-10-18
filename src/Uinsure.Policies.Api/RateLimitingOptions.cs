namespace Uinsure.Policies.Api;

// Bound from the "RateLimiting" section. Defaults live in appsettings.json; Development raises them.
internal sealed class RateLimitingOptions
{
    public RateLimit Global { get; set; } = new();

    public RateLimit Writes { get; set; } = new();
}

internal sealed class RateLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}
