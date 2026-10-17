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
- `UnexpectedExceptionHandler : IExceptionHandler` maps `PersistenceConflictException` to 409 and anything
  else to a generic 500. It logs the exception and never puts its message or stack trace in the body
  outside Development. Register it with `AddExceptionHandler<…>()` and `UseExceptionHandler()`.

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
- Pipeline: `UseExceptionHandler()` → `UseHttpsRedirection()` → (HSTS outside Development) →
  `UseAuthorization()` → `MapControllers()`.
- `ILogger<T>` with message templates: `logger.LogInformation("Policy {Reference} sold", reference)`.
  Never interpolate, and never log names, dates of birth, addresses, postcodes or bodies.
- App Insights: `if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
  builder.Services.AddOpenTelemetry().UseAzureMonitor();`.
- `public partial class Program;` at the end of `Program.cs` if the tests can't see `Program`.

## Build

- `Directory.Build.props`: `TreatWarningsAsErrors`, `Nullable` enabled, `ImplicitUsings` enabled, and
  BannedApiAnalyzers for every `src` project.
- Versions are pinned in `Directory.Packages.props` only.
- `dotnet build Uinsure.Policies.slnx -warnaserror` must be clean before every commit.
