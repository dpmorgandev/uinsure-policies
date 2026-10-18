---
name: dotnet10-webapi
description: >-
  Only the .NET 10 Web API techniques this project uses: controllers and DataAnnotations, strict JSON,
  the unknown-query filter, ProblemDetails mapping, TimeProvider, the EF Core provider switch, health, logging.
user-invocable: false
---
# .NET 10 Web API: what we build, and only that

The architecture skill (with `signatures.md` and `api.md`) says *what* to build. This skill says *how* in
.NET 10. If something isn't here or there, don't add it.

## Controllers and DTOs

- `[ApiController]`, `sealed`, `: ControllerBase`, `[Route("policies")]`. One `PoliciesController` holds sell,
  get, cancel, quote, claims and renew.
- Actions take and return only `Contracts` DTOs and `ActionResult<T>`, and they're `async` with a
  `CancellationToken`.
- Shape validation uses DataAnnotations on the DTOs (`[Required]`, `[MaxLength]`, `[RegularExpression]`).
  Required members are **nullable** (`DateOnly? StartDate`), so a missing field fails `[Required]`
  instead of binding as a default.
- DTO ≠ entity. `PolicyMappings` maps both ways with plain C#, not a mapping library.
- E1, E5 and E6 return `201` with `Location` (`CreatedAtAction` or `Created(uri, body)`). E3 returns `201`
  without `Location` (`StatusCode(201, body)`).
- Cancel body: `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelPolicyRequest? request`.

## Strict input

```csharp
builder.Services.AddControllers(o => o.Filters.Add<RejectUnknownQueryParametersFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    });
```
- camelCase is the default. `DateOnly` serialises as `yyyy-MM-dd` natively.
- `RejectUnknownQueryParametersFilter` (an `IActionFilter`) compares the request's query keys
  case-insensitively with the action's bound parameter names. For any unknown key, add
  `ModelState.AddModelError(key, "Unknown query parameter.")` and return `ValidationProblem()`.
- Normalise the reference (trim, upper-case) before validating its pattern, so `abc-1` and `ABC-1` are the
  same policy.

## Errors (RFC 9457)

- `AddProblemDetails()`, plus `CustomizeProblemDetails` to add `traceId` (`Activity.Current?.Id ??
  HttpContext.TraceIdentifier`) and `instance` (the request path).
- `ProblemCatalog` holds RuleId → (status, type, title). `ResultExtensions.ToActionResult()` turns a
  `Result<T>` into the success response or a `ProblemDetails` with `ruleId`, `violations[]` and extensions.
  This is the only place a status code is chosen for a rule.
- `UnexpectedExceptionHandler : IExceptionHandler` maps `PersistenceConflictException` to 409,
  `Microsoft.AspNetCore.Http.BadHttpRequestException` to its own `StatusCode` (413, 408, 400) with a fixed
  title and detail per status, logged at Warning, and anything else to a generic 500 logged at Error. It
  never puts an exception's message or stack trace in the body outside Development (a
  `BadHttpRequestException` message names the byte limit, so not even then). Register it with
  `AddExceptionHandler<…>()` and `UseExceptionHandler()`.

## Time

- Register `builder.Services.AddSingleton(TimeProvider.System)`. Inject `TimeProvider` into
  `PolicyService` only.
- `UkCalendar.Today(clock)`: `TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
  TimeZoneInfo.FindSystemTimeZoneById("Europe/London"))`, then `DateOnly.FromDateTime(…)`.
- Never use `DateTime.Now`, `UtcNow` or `Today`, `GetLocalNow()` or `TimeZoneInfo.Local`. These are banned
  by the analyser.

## EF Core provider switch (Persistence)

```csharp
public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
{
    services.AddOptions<DatabaseOptions>().Bind(config.GetSection("Database")).ValidateDataAnnotations().ValidateOnStart();
    services.AddDbContext<PoliciesDbContext>((sp, o) =>
    {
        var cs = config.GetConnectionString("Policies");
        _ = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider switch
        {
            DatabaseProvider.Sqlite => o.UseSqlite(cs),
            DatabaseProvider.SqlServer => o.UseSqlServer(cs),
            _ => throw new InvalidOperationException("Unknown database provider."),
        };
    });
    services.AddScoped<IPolicyRepository, PolicyRepository>();
    services.AddHealthChecks().AddDbContextCheck<PoliciesDbContext>(tags: ["ready"]);
    return services;
}
```
- Load the aggregate whole: owned types or explicit `Include`, no lazy loading. Money is
  `HasPrecision(18,2)`, enums use `HasConversion<string>()`, and `Reference` gets a unique index.
- `Version` is `IsConcurrencyToken()`. Translate `DbUpdateConcurrencyException` and the unique-index
  `DbUpdateException` into `PersistenceConflictException` in `SaveChanges`.
- With Sqlite, call `EnsureCreated()` at start-up. Never call `Database.Migrate()`. Migrations come from
  `PoliciesDbContextFactory` (SQL Server).

## Health, OpenAPI and logging

- `app.MapHealthChecks("/health/live", new() { Predicate = _ => false })` and
  `app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") })`.
- `builder.Services.AddOpenApi()`. Map `MapOpenApi()` and `MapScalarApiReference()` inside
  `if (app.Environment.IsDevelopment())` only.
- Pipeline: `UseForwardedHeaders()` → `UseExceptionHandler()` → (outside Development: `UseHsts()`, then
  reject non-HTTPS with 400 `/problems/https-required`, `/health/*` exempt) → `UseRateLimiter()` →
  `UseAuthorization()` → `MapControllers()`. **No `UseHttpsRedirection()`:** Learn says APIs should reject
  HTTP rather than redirect, because clients may not follow a redirect and the body has already crossed in
  clear.
- `ILogger<T>` with message templates: `logger.LogInformation("Policy {Reference} sold", reference)`.
  Never interpolate, and never log names, dates of birth, addresses, postcodes, bodies or client IPs.
- App Insights: `if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
  builder.Services.AddOpenTelemetry().UseAzureMonitor();`.
- `public partial class Program;` at the end of `Program.cs` if the tests can't see `Program`.

## Transport and resource limits

- Forwarded headers: `XForwardedFor | XForwardedProto`, known networks 10.0.0.0/8, 172.16.0.0/12 and
  192.168.0.0/16 (App Service's balancer), the default forward limit of 1. Never
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (it trusts any proxy, so the client IP can be spoofed).
- Body limit: `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 65_536)`, once,
  server-wide.
- Rate limiting: `AddRateLimiter` (built in, no package). A global per-IP fixed window and a named `writes`
  policy on every POST, limits bound from the `RateLimiting` configuration section, `QueueLimit = 0`.
  `OnRejected` sets 429 (the default is 503), `Retry-After` from `MetadataName.RetryAfter`, and a
  `/problems/rate-limited` ProblemDetails. `[DisableRateLimiting]` or `.DisableRateLimiting()` on
  `/health/*`. Never log the client IP.
- Launch profiles: `https` (default, `https://localhost:7080;http://localhost:5080`) for demos, and `http`
  (`http://localhost:5080`) for the loop and Postman. Both are Development, where the HTTPS check is off.

## Build

- `Directory.Build.props`: `TreatWarningsAsErrors`, `Nullable` enabled, `ImplicitUsings` enabled, and
  BannedApiAnalyzers for every `src` project.
- Versions are pinned in `Directory.Packages.props` only.
- `dotnet build Uinsure.Policies.slnx -warnaserror` must be clean before every commit.
