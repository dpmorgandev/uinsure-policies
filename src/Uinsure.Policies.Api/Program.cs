using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Uinsure.Policies.Api;
using Uinsure.Policies.Api.Errors;
using Scalar.AspNetCore;
using Uinsure.Policies.Persistence;
using Uinsure.Policies.Services;

var builder = WebApplication.CreateBuilder(args);

// One server-wide request body limit; an overflow is a 413 (see UnexpectedExceptionHandler).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65_536);

// Trust X-Forwarded-For and X-Forwarded-Proto only from private networks (App Service's balancer).
// Clearing the defaults drops loopback; never ASPNETCORE_FORWARDEDHEADERS_ENABLED, which trusts any proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

// Fixed window per client IP: a global limit, and the stricter "writes" policy on every POST. No queue.
// A request with no remote IP shares one fixed partition rather than throwing.
builder.Services.AddOptions<RateLimitingOptions>().Bind(builder.Configuration.GetSection("RateLimiting"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = WriteRateLimitedProblem;
});
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((options, limits) =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        FixedWindowPerClient(context, limits.Value.Global));
    options.AddPolicy("writes", context => FixedWindowPerClient(context, limits.Value.Writes));
});

// Strict input: unknown members, integer enums and string numbers are 400s.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ValidationProblems.FromModelState);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddScoped<PolicyService>();
builder.Services.AddOpenApi();

// RFC 9457 bodies: every problem response carries the request path and a traceId.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions["traceId"] =
        System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
});

builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.Services.EnsureSqliteCreated();

app.UseForwardedHeaders();

// Logs the exception server-side and answers a generic 500 problem; no message or stack trace in the body.
app.UseExceptionHandler();

// Outside Development an API rejects plain HTTP rather than redirecting it; /health/* is exempt.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        if (context.Request.IsHttps || context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "/problems/https-required",
                Title = "HTTPS is required.",
                Detail = "This API is served over HTTPS only. Repeat the request using https.",
            },
        });
    });
}

app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();

app.Run();

static RateLimitPartition<string> FixedWindowPerClient(HttpContext context, RateLimit limit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "no-remote-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
        });

static async ValueTask WriteRateLimitedProblem(OnRejectedContext rejected, CancellationToken cancellationToken)
{
    var context = rejected.HttpContext;
    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
    }

    await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails =
        {
            Status = StatusCodes.Status429TooManyRequests,
            Type = "/problems/rate-limited",
            Title = "Too many requests.",
            Detail = "The request rate limit was exceeded. Retry after the number of seconds in the Retry-After header.",
        },
    });
}
