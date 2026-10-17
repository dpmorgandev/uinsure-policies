# architecture.spec.md
Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Input: `docs/requirements.md` (single source of truth)

## Dependencies

**Upstream:** `docs/requirements.md` only.

**Downstream needs from this spec:**

| Spec | Needs |
|---|---|
| `api-contract` | The error-contract shape (ProblemDetails + `ruleId`), the split between 400 (shape) and 422/409 (rules), DTO placement (`Api/Contracts`), enum-as-string JSON, and the derived policy-level fields (start, end, amount) |
| `testing` | The domain's test surface (pure methods taking `today`), the `TimeProvider` seam and where `FakeTimeProvider` plugs in, the SQLite in-memory strategy for `WebApplicationFactory`, the architecture-test library and rules below |
| `infrastructure` | The app is **host-agnostic**: no App Service or container-specific code. It reads `Database:Provider` + `ConnectionStrings:Policies` from config/env, exposes `/health/live` + `/health/ready`, and runs on App Service **or** Container Apps (`dotnet publish /t:PublishContainer` needs no Dockerfile). Azure SQL is reached through managed identity (`Authentication=Active Directory Default`), so no secret. Infrastructure picks the host |
| `deployment` | Migrations are SQL Server only. They are applied with an idempotent `dotnet ef migrations script --idempotent` or a bundle, never at app start-up |
| `agents-skills` | The builder's scope: `src/` only, following the Contract below. It never adds packages outside the approved list without a `/decide` entry |

## Research

Version facts checked on NuGet/Microsoft Learn on 2026-10-17. Uncertain items are marked **(?)**.

**Platform**
- EF Core 10 latest stable is **10.0.13** (13 Oct 2026). EF 11 is at rc.2, so stay on 10 LTS. [nuget](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer)
- In ASP.NET Core 10, `Microsoft.AspNetCore.OpenApi` produces **OpenAPI 3.1 by default** and is built on OpenAPI.NET 2.0. It has breaking transformer changes (`OpenApiAny` → `JsonNode`), and nullable types emit `type: [x, "null"]`. YAML is available via `MapOpenApi("/openapi/{documentName}.yaml")`, and XML doc comments feed the document. [release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- The **.NET 10 built-in validation (`AddValidation`, `Microsoft.Extensions.Validation`) is documented for Minimal APIs and Blazor, not controllers.** Controllers still rely on `[ApiController]` automatic DataAnnotations model validation, which returns 400 `ValidationProblemDetails`. Same source, plus [error-handling-api](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0). This means the .NET 10 validation headline does not apply to our controller stack, which is worth saying in interview.
- ProblemDetails for controllers works like this: `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()` gives RFC 9457/7807 bodies for empty error responses and unhandled exceptions. It can be customised through `CustomizeProblemDetails`, a `ProblemDetailsFactory`, or `ApiBehaviorOptions.ClientErrorMapping`. In .NET 10, the exception handler middleware **no longer logs exceptions that an `IExceptionHandler` has handled** (`SuppressDiagnosticsCallback` restores that). [error-handling-api](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0), release notes
- `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) is at **10.11.0** (14 Oct 2026). [nuget](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing)

**SQLite vs SQL Server (EF Core)**
- **`decimal`** is stored as TEXT on SQLite. **Equality works, but comparison, ordering and aggregation evaluate on the client.** We never order or sum money in SQL, so there's no impact; just don't write `OrderBy(Amount)` or `Sum` in queries. On SQL Server, use `HasPrecision(18,2)`. [sqlite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- **`DateOnly`** maps to SQL Server `date` natively (EF 8+). On SQLite it's TEXT in `yyyy-MM-dd` form, which sorts lexically, so range comparisons work. EF 10 adds a `DayNumber` translation. **(?)** The exact SQL for a `>=` comparison isn't documented; check with `ToQueryString()` if a date-range query is ever written. [efcore#24506](https://github.com/dotnet/efcore/issues/24506), [sqlite functions](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/functions)
- **SQLite has no database-generated concurrency tokens**, so `rowversion` won't work there. An app-managed token (a `Guid` you change on every save) works on both. [sqlite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- **Migrations are provider-specific.** EF tooling only scaffolds for the active provider, so a second provider needs a **second migration set** (one context per provider, or one context plus separate migration assemblies). SQLite can't produce idempotent scripts. [migrations/providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)
- **Collation trap (friction, not in the docs above; from known engine defaults):** SQL Server's default collation is case-*insensitive*, and SQLite's `=` and unique index are case-*sensitive*. Without normalisation, `POL-1` and `pol-1` collide on Azure SQL but not locally, so **SELL-1 would behave differently per environment.**

**Libraries**

| Package | Version / date | Licence | Notes |
|---|---|---|---|
| FluentValidation | 12.2.0 (10 Oct 2026) | Apache-2.0 | Free. One maintainer, asks commercial users to sponsor. **(?)** I believe `FluentValidation.AspNetCore` auto-validation has been deprecated by its author, which means manual `validator.Validate` calls. Not re-verified today |
| MediatR | 14.2.0 (2 Jul 2026) | Commercial (Lucky Penny Software). Needs a licence key (`MEDIATR_LICENSE_KEY`) | Brings a key/licence conversation into a public repo, so reject |
| Scalar.AspNetCore | 2.18.0 (Oct 2026) | MIT | Targets net8/9/10 |
| Swashbuckle | not checked | MIT | Dropped from the templates in .NET 9. **(?)** Has had 3.1/OpenAPI.NET 2 support work since then. Not verified |
| NetArchTest.Rules (original) | 1.3.2 (May 2021) | (MIT per repo **(?)**) | Unmaintained for 5 years, so don't use it |
| NetArchTest.eNhancedEdition | 1.4.5 (4 Jun 2025) | MIT | netstandard2.0, works on net10 (NuGet "computed"). Same fluent API plus fixes. Last release 16 months ago |
| TngTech.ArchUnitNET(.xUnit / .xUnitV3) | 0.13.4 (20 Aug 2026) | Apache-2.0 | netstandard2.0, actively released, has an xUnit v3 package. Adds cycle detection and a richer rule language, with a steeper API |

- The original NetArchTest page lists no licence. The eNhancedEdition is MIT. [nuget](https://www.nuget.org/packages/NetArchTest.eNhancedEdition), [nuget](https://www.nuget.org/packages/TngTech.ArchUnitNET.xUnit), [nuget](https://www.nuget.org/packages/FluentValidation), [nuget](https://www.nuget.org/packages/MediatR), [nuget](https://www.nuget.org/packages/Scalar.AspNetCore)
- **NetArchTest limitation:** it checks *type dependencies*, not *method calls*. It can't enforce "`Math.Round` only in `Money`" or "no `DateTime.Now`". A banned-API analyzer (`Microsoft.CodeAnalysis.BannedApiAnalyzers` + `BannedSymbols.txt`) can, at build time. **(?)** Check the current version before adopting it.

## Options

### D1 Project structure
| Option | For | Against |
|---|---|---|
| **A. Two projects: `Domain` + `Api`** (Api holds Controllers, Application services, Persistence as folders) | Least ceremony. The only hard boundary that matters (rules have no I/O) is a project reference, which the compiler enforces. Folder boundaries are enforced by architecture tests | Persistence and controllers share an assembly, so a lazy `DbContext` in a controller compiles (the architecture test catches it) |
| B. Clean Architecture, four projects (Domain/Application/Infrastructure/Api) | Recognisable, and every boundary is a compiler boundary | Four csproj files, interfaces-for-everything (`IPolicyRepository` over `DbContext`), and mapping layers for five endpoints. That's hard to defend under "done beats perfect" |
| C. Vertical slices (one folder per feature: Sell/Cancel/Renew) in one project | Matches the F1–F4 feature loop | Rules shared across features (refund calc used by cancel + quote + CANCEL-5) need a shared home anyway. Weaker layering story when the brief asks for architecture tests |

### D2 Where rules live
| Option | For | Against |
|---|---|---|
| **A. Rich aggregate**: `Policy.Sell(...)`, `policy.Cancel(on, today)`, `policy.Renew(...)`, with a pure `RefundCalculator` | Every rule is unit-testable with no mocks, sits next to the data it protects, and maps to one rule ID per test | EF needs private parameterless constructors and backing fields, which is a small explanation cost |
| B. Anaemic entities + rule services | Familiar | Rules are spread across services, invariants can be bypassed, and tests need more setup |

### D3 Time
| Option | For | Against |
|---|---|---|
| **A. Domain takes `DateOnly today` as a parameter.** The application service derives it once from the injected `TimeProvider` via `UkCalendar.Today(timeProvider)` (UTC → Europe/London) | The domain is pure, so tests just pass a date. `FakeTimeProvider` is used at the service and integration levels. The one timezone conversion lives in one place | The requirements text says unit tests use an injected `TimeProvider`. This satisfies the intent but not the letter (see Open questions) |
| B. Inject `TimeProvider` into domain methods | Literal fit to the requirements | Every domain test news up a fake clock, and the timezone logic gets duplicated or hidden |

Note on the timezone: `TimeProvider.GetLocalNow()` uses the *server's* zone, and Azure hosts run UTC. Between 00:00 and 01:00 BST, "today" would be yesterday, which shifts SELL-2, CANCEL-3 and RENEW-1 by a day. Use `TimeZoneInfo.FindSystemTimeZoneById("Europe/London")` (IANA ids work on Windows with ICU and on Linux). This is a talking point.

### D4 Persistence / provider switching
| Option | For | Against |
|---|---|---|
| **A. One `PoliciesDbContext`, provider chosen by `Database:Provider` config. SQL Server migrations are the only migration set. SQLite (local + tests) uses `EnsureCreated()`** | One migration set to maintain, and it's for the real target. Local dev stays zero-install | The SQLite schema comes from the model, not the migrations, so migrations aren't exercised locally. Mitigation: generate the idempotent script during the git hook or test run to prove it builds |
| B. Two migration sets (SQLite + SQL Server assemblies, or a derived context per provider) | Migrations are exercised everywhere | Every model change means two `migrations add` commands, so double the diff noise in a history that's meant to show TDD |
| C. SQL Server locally (LocalDB/container) instead of SQLite | One provider and no drift | Contradicts the requirements stack line. LocalDB is Windows-only, and the repo must run for reviewers |

### D5 Validation
| Option | For | Against |
|---|---|---|
| **A. DataAnnotations on request DTOs (shape → 400 via `[ApiController]`). Business rules in the domain (→ 422/409)** | Built in, no dependency, one obvious line between "malformed" and "breaks a rule" | Weak for cross-field rules, which belong in the domain anyway |
| B. FluentValidation 12 | Expressive, testable validators | Adds a dependency and (likely) manual invocation, and duplicates domain rules if overused |
| C. .NET 10 `AddValidation` | New | Minimal APIs only, and we use controllers |

### D6 Error contract
| Option | For | Against |
|---|---|---|
| **A. Hand-rolled `Result<T>` / `RuleViolation(RuleId, Kind, Message)` returned by the domain. The controller maps it to ProblemDetails in one extension method. `IExceptionHandler` handles only the unexpected (500) and the unique-index race (`DbUpdateException` → 409 SELL-1)** | Failures show in the signatures, so there are no exceptions for expected outcomes. One mapping table from `Kind` to status. About 40 lines, no library | Slightly more code than throwing. You have to explain why you didn't use a library (FluentResults/ErrorOr) |
| B. Domain throws `RuleViolationException(RuleId, …)`, and one `IExceptionHandler` maps it | Fewest lines, and `Assert.Throws` reads well | Exceptions as control flow for expected business outcomes is the classic interview challenge |
| C. Result library (ErrorOr / FluentResults) | Ready-made | A dependency for something that's about 40 lines, and less explainable line by line |

### D7 Money
| Option | For | Against |
|---|---|---|
| **A. `decimal` plus a static `Money.Round(decimal)` (2dp, `AwayFromZero`), the only place that rounds (CANCEL-6). Enforced by a banned-API rule on `Math.Round`/`decimal.Round` outside `Money`** | Simple, and the "one place" claim is mechanically enforced | Amounts are plain decimals, so nothing at type level stops an unrounded value being stored |
| B. `Money` value object (record struct) with an owned EF mapping | Type safety | More mapping and JSON-converter code, and more to explain. Single currency (GBP) makes the benefit small |

### D8 Architecture-test library
| Option | For | Against |
|---|---|---|
| **A. NetArchTest.eNhancedEdition 1.4.5** | Readable fluent rules, MIT, works on net10 via netstandard2.0 | Last released Jun 2025. Type-level only |
| B. ArchUnitNET 0.13.4 | Actively maintained, cycle detection, xUnit v3 package | Heavier API, and nothing here needs cycle detection with two projects |
| C. No library: reflection over `Assembly.GetReferencedAssemblies()` and namespaces | Zero dependency, about 20 lines | Hand-rolled. Namespace dependency checks need IL inspection, which is what Mono.Cecil does for the libraries |

Plus, for all options, **BannedApiAnalyzers** for call-level rules (`DateTime.Now`/`UtcNow`/`Today`, `DateOnly.FromDateTime(DateTime.Now)`, `Math.Round` outside `Money`).

### D9 OpenAPI UI
| Option | For | Against |
|---|---|---|
| **A. `Microsoft.AspNetCore.OpenApi` (3.1) + Scalar 2.18, Development only** | The Microsoft-default pipeline since .NET 9, plus a modern UI | Scalar is a third-party UI |
| B. Swashbuckle (generator + UI) | Familiar to reviewers | No longer the template default, and it duplicates the built-in generator |
| C. Built-in doc only, with Postman as the UI | Fewest packages | No in-browser exploration for reviewers |

## Recommendation

- **D1 → A (Domain + Api).** The only boundary worth a compiler wall is "rules have no I/O". Everything else is a folder rule that the architecture tests guard.
- **D2 → A (rich aggregate).** One rule maps to one method maps to one test, which is the shape TDD by rule ID wants.
- **D3 → A (`today` passed in, `TimeProvider` at the service level, Europe/London in one place).** Pure domain, plus a real timezone bug avoided and explained.
- **D4 → A (single SQL Server migration set, SQLite `EnsureCreated`).** Half the migration churn. The drift risk is stated and mitigated.
- **D5 → A (DataAnnotations for shape, domain for rules).** Zero dependencies, and a clear 400-vs-422 line.
- **D6 → A (hand-rolled Result + ProblemDetails with `ruleId`).** Expected outcomes aren't exceptions, and it's about 40 lines you can explain.
- **D7 → A (`decimal` + `Money.Round` + banned-API enforcement).** CANCEL-6's "one place" becomes a receipt, not a promise.
- **D8 → A (NetArchTest.eNhancedEdition) + BannedApiAnalyzers.** Simplest readable rules. Switch to ArchUnitNET only if maintenance becomes a concern.
- **D9 → A (built-in OpenAPI 3.1 + Scalar in Development).** The current Microsoft path.
- **Logging →** built-in `ILogger` with message templates (structured), console locally, and the Azure Monitor OpenTelemetry exporter in environments (infrastructure owns the wiring). **Never log policyholder names or dates of birth.** Log reference, rule ID and outcome only. Serilog rejected as unnecessary.
- **Health →** `AddHealthChecks().AddDbContextCheck<PoliciesDbContext>()` (`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` **(?)** version not checked). `/health/live` (no DB) and `/health/ready` (DB).
- **Config/secrets →** `appsettings.json` with `Database:Provider` and a SQLite connection string for Development. In environments, a connection string from app settings/env using managed identity, so there's no password anywhere. Options classes bound with `ValidateOnStart()`. No user-secrets needed.
- **Concurrency →** an app-managed `Guid Version` concurrency token on `Policy` (works on SQLite, unlike `rowversion`), so two concurrent cancels can't issue two refunds. `DbUpdateConcurrencyException` → 409. About 5 lines.
- **SELL-1 collation →** normalise the reference in the domain (trim + upper-invariant), so uniqueness is identical on SQLite and Azure SQL.

### Considered and rejected (to record in `docs/decisions.md`)
- **Domain events / event-driven:** rejected for the build, kept as a **talking point**. `PolicyCancelled` / `PolicyRenewed` are the natural seams where real refund and payment collection (out of scope) would hang, done properly through an **outbox** (`SaveChanges` + event in one transaction) to Azure Service Bus. An in-process dispatcher without a consumer would be machinery nothing uses (§4 over-building guard). It's the job spec's nice-to-have, so name it, don't build it.
- **Caching:** rejected. Reads are single-row by unique key, and cache invalidation on cancel/renew would add bugs without measurable benefit.
- **CQRS / MediatR:** rejected. MediatR 13+ is commercial and needs a licence key. CQRS buys nothing at five endpoints. The quote (COULD-1) already shows a natural read path reusing the write-side calculator.
- **Repository + Unit of Work over EF:** rejected. `DbContext` already is both. Services use it directly.
- **AutoMapper:** rejected. Same commercial licence vendor, and hand-written `ToResponse()` mappings are explicit.
- **Auth:** out of scope by requirement. State it in the README and the security spec (OWASP API2:2023), and keep `UseAuthorization()` placement ready.

## Decision — DECIDED 2026-10-17 (Contract to be rewritten for 4 projects)

- [x] **D1 Project structure: DECIDED (David, 2026-10-17). Four projects:** `Uinsure.Policies.Api` (controllers, contracts, error mapping, composition root), `Uinsure.Policies.Services` (application services, `UkCalendar`), `Uinsure.Policies.Persistence` (`PoliciesDbContext`, configurations, migrations), and `Uinsure.Policies.Domain` (aggregate and rules, BCL only). This overrides recommendation A.
  - **Dependency direction: inverted.** Services owns one narrow `IPolicyRepository` (`GetByReference`, `Add`, `SaveChanges`), and Persistence implements it with EF. So Api → Services → Domain, Api → Persistence → Domain, and Persistence → Services for the interface. Services has no EF reference, and Api is the composition root (DI wiring only). This reverses the earlier "Repository over EF: rejected" for this one interface. There's still no Unit of Work abstraction and no generic repository.
- [x] **D2: A, rich aggregate (David, 2026-10-17).** Rules live on `Policy`, alongside a pure `RefundCalculator`. Services only orchestrate: load via the repository, compute today, call the domain, then save.
- [x] **D3: A (David, 2026-10-17).** The domain takes `DateOnly today`. Services compute it once via `UkCalendar.Today(TimeProvider)`. `FakeTimeProvider` is used in the service and integration tests. Reword requirements.md §3 to match. **Business timezone: Europe/London.**
- [x] **SELL-3 on a 29 Feb start (David, 2026-10-17):** End = 28 Feb of the next year, because the anniversary is treated as 1 Mar. One special case in `Term.End`, with a named test. Every other date uses `Start.AddYears(1).AddDays(-1)`, so 1 Mar 2027 → 29 Feb 2028.
- [x] **SELL-5 for a policyholder born on 29 Feb: no ruling needed.** A 16th birthday always falls in a leap year, because a leap year plus 16 is another leap year (the only exception is 2100), so `AddYears(16)` is exact.
- [x] **SELL-1 reference format (David, 2026-10-17):** trim and upper-case it, then it must match `^[A-Z0-9-]{3,20}$`, or the API returns 400. `abc-1` and `ABC-1` are the same policy, so the second one gets a 409. This settles the reference-normalisation checkbox below as accepted.
- [x] **D4 → A (David).** D4 Single SQL Server migration set + SQLite `EnsureCreated` (A) / dual migrations (B) / SQL Server locally (C)
- [x] **D5 → A (block).** D5 DataAnnotations + domain (A) / FluentValidation (B)
- [x] **D6 → A (David).** `Result<T>` holds a *list* of `RuleViolation` (per api-contract D7). D6 Hand-rolled Result (A) / exceptions (B) / library (C)
- [x] **D7 → A (block).** D7 `decimal` + `Money.Round` (A) / `Money` value object (B)
- [x] **D8 → A + BannedApiAnalyzers yes (block).** D8 NetArchTest.eNhancedEdition (A) / ArchUnitNET (B) / reflection (C). BannedApiAnalyzers yes/no
- [x] **D9 → A (block).** D9 OpenAPI 3.1 + Scalar (A) / Swashbuckle (B) / none (C)
- [x] **App-managed concurrency token: include (block)**
- [x] **Accepted (see SELL-1 ruling above).** Reference normalisation (upper-case): accept / reject (if rejected, pick a case-sensitive collation on Azure SQL instead)
- [x] **Domain events: rejected, kept as a talking point (block).** Caching, CQRS/MediatR and AutoMapper are rejected too. Logging is as recommended.
- [x] **Derived from D1 (to apply when the Contract is written):** the folder tree becomes four `src` projects. The layer rules are rewritten as project-reference rules: Domain → BCL only; Services → Domain; Persistence → Services + Domain + EF; Api → all three (composition root only).

## Contract — PENDING (DRAFT outline)

### Folder tree
```text
src/
  Uinsure.Policies.Domain/            # no package refs beyond the BCL
    Policies/  Policy.cs (aggregate root) Term.cs Policyholder.cs Property.cs
               Payment.cs Refund.cs Claim.cs Cancellation.cs
               PolicyType.cs PaymentMethod.cs PolicyReference.cs
    Rules/     RefundCalculator.cs  RuleIds.cs (const strings "SELL-2" ...)
    Common/    Result.cs  RuleViolation.cs  ViolationKind.cs  Money.cs
  Uinsure.Policies.Api/
    Controllers/   PoliciesController.cs (sell, get, cancel, quote, renew, claims)
    Contracts/     request/response DTOs (DataAnnotations) + mapping extensions
    Application/   PolicyService.cs  UkCalendar.cs  ReferenceGenerator.cs
    Persistence/   PoliciesDbContext.cs  Configurations/*.cs  Migrations/ (SQL Server)
    Errors/        ResultExtensions.cs (Result → ProblemDetails)  UnexpectedExceptionHandler.cs
    Program.cs
tests/
  Uinsure.Policies.Domain.Tests/  Uinsure.Policies.Api.Tests/ (integration)
  Uinsure.Policies.Architecture.Tests/  postman/
```

### Key types (signatures only)
- `Policy.Sell(PolicyReference, PolicyType, DateOnly start, decimal amount, bool autoRenew, IReadOnlyList<Policyholder>, Property, PaymentMethod, string paymentRef, DateOnly today) → Result<Policy>`. This covers SELL-2..6. SELL-1 is checked in the service, with the DB index as a backstop.
- `policy.QuoteCancellation(DateOnly on, DateOnly today) → Result<RefundQuote>` (COULD-1), and `policy.Cancel(DateOnly? on, DateOnly today, Func<string> refundRef) → Result<Cancellation>`. Both use `RefundCalculator`.
- `policy.Renew(decimal amount, PaymentMethod? method, string? paymentRef, DateOnly today) → Result<Term>` covers RENEW-1..6 and COULD-3. `policy.RecordClaim(...)` covers COULD-2.
- `RefundCalculator.ForTerm(Term, DateOnly cancelOn, bool hasClaims) → decimal` is pure and calls `Money.Round` exactly once.
- `Term.End = Start.AddYears(1).AddDays(-1)`. On a 29 Feb start, `AddYears` clamps to 28 Feb, so End = 27 Feb. **This must be confirmed against SELL-3's intent in a test.**
- Policy-level `StartDate`/`EndDate`/`Amount` are **derived** from terms (first start, last end, current term amount). They're not stored.
- `RuleViolation(string RuleId, ViolationKind Kind, string Message)`, where `Kind ∈ {NotFound→404, Conflict→409, BusinessRule→422}`.
- ProblemDetails body: `type`, `title`, `status`, `detail` (a plain-English sentence naming the rule), plus extensions `ruleId` and `traceId`.

### Layer rules (architecture tests, NetArchTest-style)
1. `Domain` has no dependency on `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore*`, `Microsoft.Extensions.*`, or `Uinsure.Policies.Api*`.
2. `Domain` has no dependency on `System.TimeProvider` (time comes in as `DateOnly`).
3. Types in `Api.Controllers` have no dependency on `Api.Persistence` or `Microsoft.EntityFrameworkCore` (they go through `Application`).
4. Types in `Api.Contracts` have no dependency on `Api.Persistence`. Domain types never appear in controller signatures, only Contracts.
5. `Api.Application` has no dependency on `Api.Controllers` or `Microsoft.AspNetCore.Mvc`.
6. Classes in `Api.Controllers` inherit `ControllerBase`, are named `*Controller`, and are sealed.
7. Domain entity types have no public setters **(?)** (check the eNhancedEdition API, or do it via reflection).
8. No `src` assembly references any test assembly or anything under `.claude/`.
- Build-time (BannedApiAnalyzers): `DateTime.Now/UtcNow/Today` and `DateTimeOffset.Now/UtcNow` banned in `src` (use `TimeProvider`). `Math.Round`/`decimal.Round` banned except in `Money`.

### Approved packages (builder may not add others without `/decide`)
`Microsoft.EntityFrameworkCore.Sqlite`, `.SqlServer`, `.Design` (10.0.x) · `Microsoft.AspNetCore.OpenApi` (10.0.x) · `Scalar.AspNetCore` · `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` · tests: `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`, `NetArchTest.eNhancedEdition`, xUnit · analyzers: `Microsoft.CodeAnalysis.BannedApiAnalyzers`.

## Open questions

1. **SELL-3 on 29 Feb:** `AddYears(1)` gives 28 Feb, so End = 27 Feb (a 365-day term, missing the 28th). Is the intent instead "the day before the anniversary", with the anniversary of 29 Feb treated as 1 Mar, giving End = 28 Feb? This needs a ruling and a `/decide` entry. **This may refine requirements.md SELL-3.**
2. **Business timezone:** confirm "today" = Europe/London. This adds an assumption to requirements.md.
3. **Requirements §3 says unit tests use an "injected `TimeProvider`".** D3-A passes `DateOnly today` into the domain and keeps `TimeProvider`/`FakeTimeProvider` at the service and integration levels. If accepted, reword §3 to match.
4. **Reference case-sensitivity (SELL-1):** normalise to upper-case, or reject a lower-case reference? This adds an interpretation to SELL-1.
5. **400 vs 422 boundary** (for api-contract): are SELL-4 (1–3 holders) and the postcode max of 8 shape (400, DataAnnotations) or rules (422, domain)? Requirements say every *rule violation* is a ProblemDetails naming the rule, and 400 `ValidationProblemDetails` carries no `ruleId` unless customised.
6. **Multiple violations at once** (for example, a backdated start *and* a 15-year-old holder): report the first, or all of them? This affects the `Result` shape.
7. **Uncertain items to verify before F0:** whether FluentValidation.AspNetCore is deprecated (only matters if D5-B), the Swashbuckle .NET 10 status (only if D9-B), the HealthChecks.EntityFrameworkCore and BannedApiAnalyzers versions, and the NetArchTest public-setter rule API.
