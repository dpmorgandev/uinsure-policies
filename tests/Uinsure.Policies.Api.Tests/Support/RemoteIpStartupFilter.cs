using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Uinsure.Policies.Api.Tests.Support;

// The in-process TestServer leaves the connection's remote IP unset. This sets it from a test-only header,
// before the app's own pipeline runs (so before forwarded headers), and removes the header. A request
// without the header keeps no remote IP, as under TestServer.
internal sealed class RemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var value))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(value.ToString());
                context.Request.Headers.Remove(Header);
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
