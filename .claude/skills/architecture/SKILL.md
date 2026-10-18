---
name: architecture
description: >-
  The architecture contract for Uinsure.Policies: projects, references, key types, where rules live,
  HTTP edge, persistence, cross-cutting rules, architecture tests, banned APIs and approved packages.
user-invocable: false
---
# Architecture contract

Binding for all `src/` and `tests/` work. Read `signatures.md` before touching a Domain or Services type, and `api.md` (routes, rule × status matrix, payloads) before any API or test work.

## Folder tree

```text
Uinsure.Policies.slnx  global.json  Directory.Build.props  Directory.Packages.props  BannedSymbols.txt
src/Uinsure.Policies.Domain/        BCL only: no PackageReference, no ProjectReference
  Policies/  Policy.cs (aggregate root) Term Policyholder Property Payment Refund Claim Cancellation
             PolicyReference PolicyType PaymentMethod PolicyStatus
  Rules/     RefundCalculator RefundCalculation RuleIds
  Common/    Result RuleViolation ViolationKind Money
src/Uinsure.Policies.Services/      IPolicyService ← PolicyService (orchestrates only: load → today → domain → save)
  Commands/SellPolicy, Commands/PolicyDetails (read model)  IPolicyRepository  PersistenceConflictException
  UkCalendar  IReferenceGenerator ← ReferenceGenerator (payment and refund references, injected)
src/Uinsure.Policies.Persistence/   PoliciesDbContext  PoliciesDbContextFactory  Records/ (internal *Record)  PolicyRecordMapper
  Configurations/ (map the records, never Domain)  Migrations/ (SQL Server)  PolicyRepository (internal sealed)  DatabaseOptions
src/Uinsure.Policies.Api/           Program.cs (composition root: the only Api type that touches Persistence)
  Controllers/PoliciesController (sell, get, cancel, quote, claims, renew)
  Contracts/ (DTOs with DataAnnotations, PolicyMappings)
  Errors/ProblemCatalog (RuleId → status, type, title), ResultExtensions, UnexpectedExceptionHandler
  Filters/RejectUnknownQueryParametersFilter   Properties/launchSettings.json (port 5080)
src/web/                            reserved, empty
tests/ Domain.Tests (pure, DateOnly today) · Services.Tests (only if needed) · Api.Tests (WebApplicationFactory
       + SQLite in-memory + FakeTimeProvider) · Architecture.Tests · postman/   (all Uinsure.Policies.*)
```

## References

| Project | May reference | Must not reference |
|---|---|---|
| Domain | BCL | any Uinsure project, any package, `System.TimeProvider` |
| Services | Domain | Persistence, Api, `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore*` |
| Persistence | Services, Domain, EF Core packages, HealthChecks.EntityFrameworkCore | Api, `Microsoft.AspNetCore*` |
| Api | Services, Persistence, Domain, approved Api packages | any test project |
| *.Tests | the project under test (Architecture.Tests: all four) | — |

- Nothing in `src/` or `tests/` references `.claude/`.
- No generic repository and no Unit of Work. Interfaces at the layer seams only: `IPolicyService`, `IPolicyRepository`, `IReferenceGenerator`.
- A model per layer: Api DTOs ↔ Domain ↔ Persistence records. `Policy.Restore` is for Persistence only; `Version` is on `PolicyRecord`.

## Where rules live (signatures in `signatures.md`)

- `Policy.Sell` (SELL-2..7) · `policy.QuoteCancellation` (COULD-1) · `policy.Cancel` (CANCEL-1..7, COULD-2)
  · `policy.Renew` (RENEW-1..6, COULD-3, RENEW-3b) · `policy.RecordClaim` (COULD-2a..c).
- `RefundCalculator.ForTerm(term, cancelOn, hasClaims)` is pure. `RuleApplied` is one of `CANCEL-1a`,
  `CANCEL-1b`, `CANCEL-1c`, `COULD-2` or `UNPAID-TERM`. `UNPAID-TERM` means `term.Payment == null`, gives
  amount 0, and wins over COULD-2. `Money.Round` is called exactly once.
- `Term.EndFor(start)`: a 29 Feb start ends on 28 Feb of the next year; otherwise `start.AddYears(1).AddDays(-1)`.
  `Term.Days = End − Start + 1` (365 or 366).
- `PolicyReference.Normalise` = `Trim().ToUpperInvariant()`. The pattern is checked at the 400 level.
- Status (`StatusOn(today)`): `Cancelled` if there's a cancellation; `Pending` if today < first start;
  `Expired` if today > `LatestTerm.End`; else `Active`. `CurrentTerm(today)` = the term covering today, else `LatestTerm`.
- `SELL-1` is checked in the service via `GetByReference`. `CANCEL-4`, `RENEW-6` and `COULD-2c` are domain conflicts.

**Evaluation order (every command):** 400 shape (model binding) → 404 `GET-1` (service) → 409 conflict,
each returned alone → then every 422 rule is collected, and none returns early.

**Domain rules**
- Windows measure from `LatestTerm`. RENEW-1/2: `today ∈ [LatestTerm.End − 30, LatestTerm.End]`.
  CANCEL-7: `on ≤ LatestTerm.End`. RENEW-4: the new term starts on `LatestTerm.End + 1`.
- RENEW-3a: `method ?? CurrentTerm(today).Payment.Method`. COULD-3 applies to an explicit or a defaulted
  Cheque. RENEW-3b: `AutoRenew == false` with a `method` gives a 422, and the term is unpaid.
- Cancel: `on ?? today`. Each refund goes to the method of the payment it reverses (CANCEL-2).
- **No £0 Refund.** A `Refund` is created only when `RefundCalculation.Amount > 0`. Every term's
  calculation is still returned.
- Any claim sets `HasClaims = true`. The repository sets `PolicyRecord.Version = Guid.NewGuid()` on every
  save of a changed policy (Cancel, Renew, RecordClaim); the domain has no `Version`.

## HTTP edge

| Source | Status | Body |
|---|---|---|
| Model binding / DataAnnotations / strict input | 400 | `ValidationProblemDetails` (`errors`) + `traceId`, no `ruleId` |
| `ViolationKind.NotFound` | 404 | ProblemDetails + `ruleId` |
| `ViolationKind.Conflict` | 409 | ProblemDetails + `ruleId` |
| `ViolationKind.BusinessRule` | 422 | + `ruleId` (first in `RuleIds.Order`) + `violations: [{ruleId, detail}]` |
| `PersistenceConflictException(DuplicateReference)` | 409 | as SELL-1 |
| `PersistenceConflictException(Concurrency)` | 409 | `type: /problems/concurrency`, no `ruleId`, detail asks to retry |
| Transport: `BadHttpRequestException` · non-HTTPS outside Development (not `/health/*`) · rate limiter | its 413/408/400 · 400 · 429 + `Retry-After` | no `ruleId`; fixed title/detail, never the exception message (Warning) · `/problems/https-required` · `/problems/rate-limited` |
| any other exception | 500 | generic `detail` + `traceId`; exception to logs only; no stack trace outside Development |

- RFC 9457 body: `type` = `/problems/{rule-id lower-case, "/" → "-"}`, `title`, `status`, `detail`, `instance`,
  `ruleId`, `traceId`, plus `RuleViolation.Extensions`. `ProblemCatalog` maps status, type and title in one
  place; `ResultExtensions` builds the response in one place.
- Controllers are `[ApiController]`, `sealed`, `: ControllerBase` and `[Route("policies")]`. Public action
  signatures use only `Contracts` types and `ActionResult`, never Domain types.
- **Strict input → 400.**
  - JSON: `UnmappedMemberHandling = Disallow`, `JsonStringEnumConverter(allowIntegerValues: false)`,
    camelCase, strict numbers. `DateOnly` is `yyyy-MM-dd`.
  - `RejectUnknownQueryParametersFilter` is global: an unbound query key (case-insensitive) gives `errors[key]`.
  - Required DTO members are nullable and `[Required]`.
  - Reference (body and route), trimmed and upper-cased, matches `^[A-Z0-9-]{3,20}$`. Postcode ≤ 8; claim `description` ≤ 500.
  - The cancel body is optional: `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]`.

## Persistence

- There is one `PoliciesDbContext`. The provider comes from `Database:Provider` (`Sqlite` | `SqlServer`),
  and the connection from `ConnectionStrings:Policies`. `services.AddPersistence(configuration)` registers
  the context, `IPolicyRepository` and the `ready` health check.
- The aggregate is loaded whole, with no lazy loading. There's a unique index on `Reference`. Money uses
  `HasPrecision(18,2)`, and enums are stored as strings.
- EF maps the internal `*Record` types; `PolicyRecord.Version` is `.IsConcurrencyToken()`. `SaveChanges`
  maps domain → tracked record (a per-request map, never static), then translates EF conflicts to `PersistenceConflictException`.
- Migrations are SQL Server only, made via `PoliciesDbContextFactory`. The app never calls `Database.Migrate()`:
  with Sqlite it calls `EnsureCreated()` at start-up, and the tests do too.
- Never `OrderBy`, `Sum` or compare money in SQL, because SQLite stores decimal as TEXT.

## Cross-cutting

- **Time:** only `UkCalendar.Today(TimeProvider)` produces today, in Europe/London. `TimeProvider.System` is
  registered in Program. Tests use `FakeTimeProvider`.
- **Logging:** `ILogger` message templates only. Log the reference, rule IDs, outcome and traceId.
  **Never log** names, dates of birth, address lines, postcodes or request bodies. App Insights is used
  only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- **Health:** `/health/live` runs no checks. `/health/ready` runs the checks tagged `ready`.
- **Config:** `appsettings.Development.json` sets Sqlite with one DB file per worktree. There are no secrets,
  no user-secrets and no Key Vault. `DatabaseOptions` is bound with `ValidateDataAnnotations().ValidateOnStart()`.
- **OpenAPI** 3.1 with Scalar, Development only. **Auth:** none, but `UseAuthorization()` stays in the pipeline.
## Architecture tests (NetArchTest.eNhancedEdition + reflection)

- **A1–A3** enforce the References table: Domain has no `Microsoft.*` or `System.TimeProvider`; Services
  has no EF Core or AspNetCore; Persistence has no AspNetCore. **A4:** no `src` assembly references
  `xunit*`, `TimeProvider.Testing`, `Mvc.Testing` or `*.Tests`.
- **A5:** `Api.*` types except `Program` don't use Persistence or EF Core. **A6:** `Api.Contracts` doesn't use AspNetCore.
- **A7:** controllers are sealed, inherit `ControllerBase`, are named `*Controller`, and have no Domain type
  in public signatures. **A8:** Domain entities have no public setters.
- **A9:** no `*UnitOfWork*` type and no generic `*Repository*` type. **A10:** `PolicyRepository` is
  `internal sealed`. **A11:** the `RS0030` suppression appears only in `Money.cs`.

**Banned APIs** (`BannedSymbols.txt`, `RS0030` as an error, in every `src` project):
- `DateTime.Now`, `UtcNow` and `Today`, `DateTimeOffset.Now` and `UtcNow`, `TimeProvider.GetLocalNow()`
  and `TimeZoneInfo.Local`. Use `UkCalendar.Today` instead.
- Every `Math.Round(decimal…)` and `decimal.Round(…)` overload. Use `Money.Round` instead.

## Approved packages (versions pinned once in `Directory.Packages.props`; no `Version` in project files)

| Package | Project |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer`, `.Sqlite`, `.Design` (PrivateAssets=all) 10.0.13 | Persistence (Sqlite also Api.Tests) |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | Persistence |
| `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` 2.18.0, `Azure.Monitor.OpenTelemetry.AspNetCore` | Api |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` (PrivateAssets=all) | all `src` |
| `xunit.v3` 4.0.2 · `Microsoft.AspNetCore.Mvc.Testing` · `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 · `NetArchTest.eNhancedEdition` 1.4.5 | tests |

**Not to be added:** FluentValidation, MediatR, AutoMapper, Swashbuckle, Serilog, FluentAssertions, a
Result/ErrorOr library, caching, domain events or outbox, Asp.Versioning, or auth packages. Any other
package needs David's approval. Out of scope: auth, pricing, payment processing, and the React frontend
(`src/web/` is reserved).
