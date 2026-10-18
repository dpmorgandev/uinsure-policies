using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Uinsure.Policies.Api.Tests.Support;

// Appends a path that throws after the app's own pipeline, so the throw happens inside the app's
// exception handling exactly as an exception from a controller would.
internal sealed class ThrowingEndpointStartupFilter : IStartupFilter
{
    public const string Path = "/test/unhandled-exception";

    public const string Sentinel = "F0B-SENTINEL-the-exception-message-must-stay-in-the-logs";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException(Sentinel)));
    };
}
