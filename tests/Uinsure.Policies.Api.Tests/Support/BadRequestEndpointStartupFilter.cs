using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Uinsure.Policies.Api.Tests.Support;

// Appends paths that throw Microsoft.AspNetCore.Http.BadHttpRequestException after the app's own pipeline,
// as Kestrel would from inside a controller's body read. Each message names the byte limit, as Kestrel's
// own oversized-body message does, and carries a per-path sentinel the body must never show.
internal sealed class BadRequestEndpointStartupFilter : IStartupFilter
{
    public const string Sentinel = "F1-D1-I3-SENTINEL";

    // A second 413 with a different message: the title and detail must not change with the message.
    public const string OtherMessage413Path = "/test/bad-request/413-other";

    public static int[] Statuses { get; } = [StatusCodes.Status400BadRequest, StatusCodes.Status408RequestTimeout, StatusCodes.Status413PayloadTooLarge];

    public static string PathFor(int status) => $"/test/bad-request/{status}";

    public static string MessageFor(int status) => $"{Sentinel}-{status}: the max request body size is 65536 bytes.";

    public static string OtherMessage413 => $"{Sentinel}-413-other: a different message for the same status, limit 65536.";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        foreach (var status in Statuses)
        {
            app.Map(PathFor(status), branch => branch.Run(_ => throw new BadHttpRequestException(MessageFor(status), status)));
        }

        app.Map(OtherMessage413Path, branch => branch.Run(_ => throw new BadHttpRequestException(OtherMessage413, StatusCodes.Status413PayloadTooLarge)));
    };
}
