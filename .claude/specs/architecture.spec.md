# architecture.spec.md
Status: DECIDED — contract final (2026-10-17)
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
| `agents-skills` | The builder's scope: `src/` only, following the Contract below. It never adds packages outside the approved list (C8) unless David approves an amendment to this spec |

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
- **Logging →** built-in `ILogger` with message templates (structured), console locally, and the Azure Monitor OpenTelemetry exporter in environments (the app-side wiring is owned by a plan node; see Contract C6). **Never log policyholder names or dates of birth.** Log reference, rule ID and outcome only. Serilog rejected as unnecessary.
- **Health →** `AddHealthChecks().AddDbContextCheck<PoliciesDbContext>()` (`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` **(?)** version not checked). `/health/live` (no DB) and `/health/ready` (DB).
- **Config/secrets →** `appsettings.json` with `Database:Provider` and a SQLite connection string for Development. In environments, a connection string from app settings/env using managed identity, so there's no password anywhere. Options classes bound with `ValidateOnStart()`. No user-secrets needed.
- **Concurrency →** an app-managed `Guid Version` concurrency token on `Policy` (works on SQLite, unlike `rowversion`), so two concurrent cancels can't issue two refunds. `DbUpdateConcurrencyException` → 409. About 5 lines.
- **SELL-1 collation →** normalise the reference in the domain (trim + upper-invariant), so uniqueness is identical on SQLite and Azure SQL.

### Considered and rejected (talking points; recorded here, not in a separate decisions file)
- **Domain events / event-driven:** rejected for the build, kept as a **talking point**. `PolicyCancelled` / `PolicyRenewed` are the natural seams where real refund and payment collection (out of scope) would hang, done properly through an **outbox** (`SaveChanges` + event in one transaction) to Azure Service Bus. An in-process dispatcher without a consumer would be machinery nothing uses (§4 over-building guard). It's the job spec's nice-to-have, so name it, don't build it.
- **Caching:** rejected. Reads are single-row by unique key, and cache invalidation on cancel/renew would add bugs without measurable benefit.
- **CQRS / MediatR:** rejected. MediatR 13+ is commercial and needs a licence key. CQRS buys nothing at five endpoints. The quote (COULD-1) already shows a natural read path reusing the write-side calculator.
- **Repository + Unit of Work over EF:** rejected. `DbContext` already is both. Services use it directly.
- **AutoMapper:** rejected. Same commercial licence vendor, and hand-written `ToResponse()` mappings are explicit. **Re-checked 2026-10-18 (David chose a mapping library for F1b):** AutoMapper 15+ is dual-licensed RPL-1.5 or commercial (a free Community edition below $5M revenue, with a registered key), and 14.x stays MIT but carries the unpatched DoS advisory GHSA-rvv3-g6hj-g44x ([launch post](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/), [ABP notes](https://abp.io/docs/latest/framework/infrastructure/luckypenny-automapper)). It also maps at run time and suits settable properties, while this Domain is built only through factories. **Mapperly is approved instead** (David, 2026-10-18): `Riok.Mapperly` 4.3.1, Apache-2.0, a compile-time source generator with no runtime dependency ([nuget](https://www.nuget.org/packages/Riok.Mapperly), fetched 2026-10-18). Mismatches fail the build. `Policy.Restore`, `Version` and the tracked-record update stay hand-written hooks inside the mappers.
- **Auth:** out of scope by requirement. State it in the README and the security spec (OWASP API2:2023), and keep `UseAuthorization()` placement ready.

## Decision — DECIDED 2026-10-17 (applied in Contract)

- [x] **D1 Project structure: DECIDED (David, 2026-10-17). Four projects:** `Uinsure.Policies.Api` (controllers, contracts, error mapping, composition root), `Uinsure.Policies.Services` (application services, `UkCalendar`), `Uinsure.Policies.Persistence` (`PoliciesDbContext`, configurations, migrations), and `Uinsure.Policies.Domain` (aggregate and rules, BCL only). This overrides recommendation A.
  - **Dependency direction: inverted.** Services owns one narrow `IPolicyRepository` (`GetByReference`, `Add`, `SaveChanges`), and Persistence implements it with EF. So Api → Services → Domain, Api → Persistence → Domain, and Persistence → Services for the interface. Services has no EF reference, and Api is the composition root (DI wiring only). This reverses the earlier "Repository over EF: rejected" for this one interface. There's still no Unit of Work abstraction and no generic repository.
- [x] **D2: A, rich aggregate (David, 2026-10-17).** Rules live on `Policy`, alongside a pure `RefundCalculator`. Services only orchestrate: load via the repository, compute today, call the domain, then save.
- [x] **D3: A (David, 2026-10-17).** The domain takes `DateOnly today`. Services compute it once via `UkCalendar.Today(TimeProvider)`. `FakeTimeProvider` is used in the service and integration tests. Reword requirements.md §3 to match. **Business timezone: Europe/London.**
- [x] **SELL-3 on a 29 Feb start (David, 2026-10-17):** End = 28 Feb of the next year, because the anniversary is treated as 1 Mar. One special case in `Term.End`, with a named test. Every other date uses `Start.AddYears(1).AddDays(-1)`, so 1 Mar 2027 → 29 Feb 2028.
- [x] **Amendment (David, 2026-10-18, after F1): a model per layer, and interfaces at the layer seams.** Reviewing F1, David asked for the layering he normally builds: Api models map to the domain, and the domain maps to Persistence models, which are what is stored. F1 showed EF leaking into the domain (private parameterless constructors and mutable `List` collections added only for EF, and `Version` on the aggregate). So:
  - **Persistence records.** `Persistence/Records/` holds `PolicyRecord`, `PolicyholderRecord`, `PropertyRecord`, `TermRecord` and `PaymentRecord`: internal, mutable, with database keys. The EF configurations map the records, never a domain type. `PolicyRecordMapper` (internal) maps domain → record on `Add` and record → domain on load, and on `SaveChanges` applies the domain's state onto the tracked record. Both it and the Api's `PolicyMappings` are Mapperly `[Mapper]` classes (compile-time generated), with `Policy.Restore`, `Version` and the tracked-record update as hand-written methods on them.
  - **`Version` lives on `PolicyRecord` only** (David). The repository keeps the record it loaded for each policy, so EF's original-value check still turns a stale save into the 409 `/problems/concurrency`. That map lives on the scoped `PolicyRepository`, one per request, and never in a static, so loaded records are released with the request's `DbContext`.
  - **`Policy.Restore(...)`** (David) is a public static factory that rebuilds stored state and runs no sale rule. Only Persistence calls it (architecture test A13).
  - **Interfaces at the layer seams.** `IPolicyService` (Services; the controller depends on it), `IReferenceGenerator` (Services; injected into `PolicyService`, which no longer calls a static), and `IPolicyRepository` unchanged. `TimeProvider` stays the time seam, so `UkCalendar` remains a static helper over it. There are no interfaces inside the Domain, and still no generic repository or Unit of Work.
  - Built as plan node **F1b** before F2 (red: architecture tests A12–A15; green: the refactor, with F1's 277 tests as the safety net and no behaviour change).
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
- [x] **Final rulings (David, 2026-10-17):**
  - OQ1: no `Refund` is created for £0: neither for a COULD-2 claim nor for an unpaid RENEW-3b term. The calculation is still returned per term, with `RuleApplied` `COULD-2` or `UNPAID-TERM`.
  - OQ3: central package management via `Directory.Packages.props`: yes (C1, C8).
  - New rule SELL-7 (amount > 0, else 422) is enforced in `Policy.Sell`.
  - Accepted as proposed: `RuleViolation` has an optional `Extensions` member, and `PersistenceConflictException` is defined in Services.
- [x] **Amendment (David, 2026-10-19, F3 review): the quote and claim results carry what the API needs.** F3's review found three seams that made the controller read twice or repeat a rule: the quote read UK today twice per request (quotedOn could disagree with the CANCEL-3 check at midnight), the Api re-implemented `Policy.Cancel`'s decision for the quote (top-level rule, no £0 refund), and the claim response re-read the policy after the save and took its last claim. The names stay as below; the return types grow:
  - `Policy.QuoteCancellation` returns `CancellationQuote` (date, top-level rule, total, per-term calculations, quoted refunds), the one decision `Cancel` also applies.
  - `PolicyService.QuoteCancellation` returns `CancellationQuoteResult(CancellationQuote, QuotedOn)`, with the one UK today; `PolicyService.RecordClaim` returns `RecordedClaim(Claim, Number, HasClaims)`, read before the save.
  - `PolicyDetails` stays `(Policy, CurrentTerm, Status)`.

## Contract

Binding. Where this section and any earlier section disagree, this section wins. The "Agent-facing extract" at the end lists what is copied into the `architecture` skill.

### C1 Folder tree
```text
Uinsure.Policies.slnx  global.json  Directory.Build.props  Directory.Packages.props  BannedSymbols.txt
src/
  Uinsure.Policies.Domain/              # BCL only: no PackageReference, no ProjectReference
    Policies/  Policy.cs (aggregate root)  Term.cs  Policyholder.cs  Property.cs  Payment.cs
               Refund.cs  Claim.cs  Cancellation.cs  PolicyReference.cs
               PolicyType.cs  PaymentMethod.cs  PolicyStatus.cs
    Rules/     RefundCalculator.cs  RefundCalculation.cs  RuleIds.cs
    Common/    Result.cs  RuleViolation.cs  ViolationKind.cs  Money.cs
  Uinsure.Policies.Services/
    PolicyService.cs                    # orchestrates only: load → today → domain → save
    Commands/  SellPolicy.cs  PolicyDetails.cs (read model returned to Api)
    IPolicyService.cs  IPolicyRepository.cs  IReferenceGenerator.cs  PersistenceConflictException.cs
    UkCalendar.cs  ReferenceGenerator.cs (payment and refund references; implements IReferenceGenerator)
  Uinsure.Policies.Persistence/
    PoliciesDbContext.cs  PoliciesDbContextFactory.cs (design-time, SQL Server)
    Records/  PolicyRecord.cs  PolicyholderRecord.cs  PropertyRecord.cs  TermRecord.cs  PaymentRecord.cs (internal)
    PolicyRecordMapper.cs (internal: domain ↔ record)
    Configurations/*.cs (map the records)  Migrations/ (SQL Server only)
    PolicyRepository.cs (internal sealed)  DatabaseOptions.cs  PersistenceServiceCollectionExtensions.cs
  Uinsure.Policies.Api/
    Program.cs                          # composition root: the only Api type that touches Persistence
    Controllers/  PoliciesController.cs (sell, get, cancel, quote, claims, renew)
    Contracts/    request/response DTOs (DataAnnotations)  PolicyMappings.cs
    Errors/       ProblemCatalog.cs (RuleId → status, type, title)  ResultExtensions.cs
                  UnexpectedExceptionHandler.cs (IExceptionHandler)
    Filters/      RejectUnknownQueryParametersFilter.cs
    appsettings.json  appsettings.Development.json  Properties/launchSettings.json (port 5080)
  web/                                  # reserved, empty; not in this plan
tests/
  Uinsure.Policies.Domain.Tests/        # unit, pure: pass DateOnly today
  Uinsure.Policies.Services.Tests/      # only if a service needs a unit test; FakeTimeProvider
  Uinsure.Policies.Api.Tests/           # integration: WebApplicationFactory + SQLite in-memory + FakeTimeProvider
  Uinsure.Policies.Architecture.Tests/
  postman/                              # v2.1 collection + environment
```

### C2 Project-reference graph

| Project | May reference | Must not reference |
|---|---|---|
| Domain | BCL | any Uinsure project, any package, `System.TimeProvider` |
| Services | Domain | Persistence, Api, `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore*` |
| Persistence | Services, Domain, EF Core packages, HealthChecks.EntityFrameworkCore | Api, `Microsoft.AspNetCore*` |
| Api | Services, Persistence, Domain, approved Api packages | any test project |
| *.Tests | the project under test (Architecture.Tests: all four) | — |

No `src/` or `tests/` project references anything under `.claude/`. No generic repository and no Unit of Work abstraction. Interfaces sit at the layer seams only: `IPolicyService`, `IPolicyRepository` and `IReferenceGenerator`, each with one implementation, and none inside the Domain (amended 2026-10-18, F1b).

### C3 Key types (signatures)
```csharp
// Domain/Common
public enum ViolationKind { NotFound, Conflict, BusinessRule }        // → 404, 409, 422
public sealed record RuleViolation(string RuleId, ViolationKind Kind, string Message,
    IReadOnlyDictionary<string, object?>? Extensions = null);         // Extensions = rule-specific ProblemDetails members (api-contract)
public sealed class Result<T>
{
    public bool IsSuccess { get; }                                     // Violations.Count == 0
    public T Value { get; }                                            // throws InvalidOperationException on failure
    public IReadOnlyList<RuleViolation> Violations { get; }            // ordered by RuleIds.Order
    public static Result<T> Success(T value);
    public static Result<T> Failure(params RuleViolation[] violations); // ≥ 1
}
public static class Money { public static decimal Round(decimal amount); } // 2dp, AwayFromZero; the only rounding call (CANCEL-6)

// Domain/Rules
public static class RuleIds { public const string Sell1 = "SELL-1"; /* … every ID in requirements.md */
    public static IReadOnlyList<string> Order { get; } }               // requirements.md order; drives "first ruleId"
public sealed record RefundCalculation(decimal Amount, string RuleApplied, int TermDays, int UsedDays, int UnusedDays);
public static class RefundCalculator
{   // pure; RuleApplied ∈ CANCEL-1a | CANCEL-1b | CANCEL-1c | COULD-2 | UNPAID-TERM (term.Payment == null; wins over COULD-2; Amount 0); Money.Round called exactly once
    public static RefundCalculation ForTerm(Term term, DateOnly cancelOn, bool hasClaims);
}

// Domain/Policies
public readonly record struct PolicyReference(string Value)
{   public static PolicyReference Normalise(string raw); }             // Trim().ToUpperInvariant(); pattern checked at 400 level
public enum PolicyStatus { Pending, Active, Expired, Cancelled }
public sealed class Term
{
    public DateOnly Start { get; }  public DateOnly End { get; }  public decimal Amount { get; }
    public Payment? Payment { get; }                                   // null = unpaid (RENEW-3b)
    public int Days => End.DayNumber - Start.DayNumber + 1;            // 365 or 366
    public bool Covers(DateOnly date) => Start <= date && date <= End;
    public static DateOnly EndFor(DateOnly start) =>                   // SELL-3
        start is { Month: 2, Day: 29 }
            ? new DateOnly(start.Year + 1, 2, 28)                      // named test: 29 Feb 2028 → 28 Feb 2029
            : start.AddYears(1).AddDays(-1);                           // 1 Mar 2027 → 29 Feb 2028
}
public sealed class Policy
{
    public PolicyReference Reference { get; }  public PolicyType Type { get; }
    public bool AutoRenew { get; }  public bool HasClaims { get; }    // no Version: it lives on PolicyRecord (F1b)
    public IReadOnlyList<Policyholder> Policyholders { get; }  public Property Property { get; }
    public IReadOnlyList<Term> Terms { get; }  public IReadOnlyList<Claim> Claims { get; }
    public Cancellation? Cancellation { get; }
    public Term LatestTerm => Terms[^1];
    public Term CurrentTerm(DateOnly today);   // the term covering today, else LatestTerm
    public PolicyStatus StatusOn(DateOnly today);
    // Cancelled if Cancellation != null; Pending if today < Terms[0].Start; Expired if today > LatestTerm.End; else Active

    public static Policy Restore(PolicyReference reference, PolicyType type, bool autoRenew, bool hasClaims,
        IReadOnlyList<Policyholder> holders, Property property, IReadOnlyList<Term> terms,
        IReadOnlyList<Claim> claims, Cancellation? cancellation);     // persistence only (A13): stored state, no rules run
    public static Result<Policy> Sell(PolicyReference reference, PolicyType type, DateOnly start, decimal amount,
        bool autoRenew, IReadOnlyList<Policyholder> holders, Property property, PaymentMethod method,
        string paymentReference, DateOnly today);                      // SELL-2..7 (SELL-7: amount > 0)
    public Result<CancellationQuote> QuoteCancellation(DateOnly? on, DateOnly today); // COULD-1; the decision Cancel applies
    public Result<Cancellation> Cancel(DateOnly? on, DateOnly today, Func<string> refundReference);  // CANCEL-1..7, COULD-2
    public Result<Term> Renew(decimal amount, PaymentMethod? method, DateOnly today, Func<string> paymentReference); // RENEW-1..6, COULD-3, RENEW-3b
    public Result<Claim> RecordClaim(DateOnly claimDate, string? description, DateOnly today);         // COULD-2a..c
}
public sealed record CancellationQuote(DateOnly CancellationDate, string RuleApplied, decimal TotalRefund,
    IReadOnlyList<RefundCalculation> Terms, IReadOnlyList<QuotedRefund> Refunds);   // amendment 2026-10-19
public sealed record QuotedRefund(PaymentMethod Method, decimal Amount, string PaymentReference);

// Services
public interface IPolicyRepository
{
    Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct);
    void Add(Policy policy);
    Task SaveChanges(CancellationToken ct);   // EF exceptions → PersistenceConflictException
}
public enum ConflictReason { Concurrency, DuplicateReference }
public sealed class PersistenceConflictException(ConflictReason reason) : Exception;
public static class UkCalendar { public static DateOnly Today(TimeProvider clock); } // GetUtcNow() → Europe/London
public sealed record PolicyDetails(Policy Policy, Term CurrentTerm, PolicyStatus Status);
public sealed record CancellationQuoteResult(CancellationQuote Quote, DateOnly QuotedOn);    // amendment 2026-10-19
public sealed record RecordedClaim(Claim Claim, int Number, bool HasClaims);               // amendment 2026-10-19
public interface IReferenceGenerator { string Payment(); string Refund(); }  // implemented by ReferenceGenerator
public interface IPolicyService { /* the six methods below; the controller depends on this */ }
public sealed class PolicyService(IPolicyRepository repository, IReferenceGenerator references, TimeProvider clock) : IPolicyService
{   // each method computes today once via UkCalendar.Today(clock)
    Task<Result<PolicyDetails>> Sell(SellPolicy command, CancellationToken ct);
    Task<Result<PolicyDetails>> Get(PolicyReference reference, CancellationToken ct);
    Task<Result<Cancellation>> Cancel(PolicyReference reference, DateOnly? on, CancellationToken ct);
    Task<Result<CancellationQuoteResult>> QuoteCancellation(PolicyReference reference, DateOnly? on, CancellationToken ct);
    Task<Result<Term>> Renew(PolicyReference reference, decimal amount, PaymentMethod? method, CancellationToken ct);
    Task<Result<RecordedClaim>> RecordClaim(PolicyReference reference, DateOnly claimDate, string? description, CancellationToken ct);
}
```

**Evaluation order (every command):** 400 shape (model binding) → 404 `GET-1` (service) → 409 conflict (SELL-1 in the service via `GetByReference`; CANCEL-4, RENEW-6, COULD-2c in the domain), each returned alone → all 422 rules collected, none returning early.

**Domain rules:**
- Window and cut-off rules measure from `LatestTerm`: RENEW-1/2 is `today ∈ [LatestTerm.End − 30, LatestTerm.End]`; CANCEL-7 is `on ≤ LatestTerm.End`.
- RENEW-4: the new term starts on `LatestTerm.End + 1`, with `End = Term.EndFor(start)`.
- RENEW-3a: `method ?? CurrentTerm(today).Payment.Method`. COULD-3 applies to an explicit or a defaulted Cheque. RENEW-3b: `AutoRenew == false` and `method != null` gives a 422, and the term is unpaid.
- Cancel: `on ?? today`. Each refund goes to the method of the payment it reverses (CANCEL-2).
- **No £0 Refund.** A `Refund` is created only for a term whose `RefundCalculation.Amount` > 0. A COULD-2 claim or an unpaid RENEW-3b term (`RuleApplied` `UNPAID-TERM`) creates none, but its `RefundCalculation` is still returned, so the response lists every term.
- Any claim sets `HasClaims = true`.
- Every successful mutation (`Cancel`, `Renew`, `RecordClaim`) sets `Version = Guid.NewGuid()`.

### C4 HTTP edge rules

| Source | Status | Body |
|---|---|---|
| Model binding / DataAnnotations / strict input | 400 | `ValidationProblemDetails` (`errors` map) + `traceId`, no `ruleId` |
| `ViolationKind.NotFound` | 404 | ProblemDetails + `ruleId` |
| `ViolationKind.Conflict` | 409 | ProblemDetails + `ruleId` |
| `ViolationKind.BusinessRule` | 422 | ProblemDetails + `ruleId` (first in `RuleIds.Order`) + `violations: [{ruleId, detail}]` |
| `PersistenceConflictException(DuplicateReference)` | 409 | as SELL-1 (unique-index race backstop) |
| `PersistenceConflictException(Concurrency)` | 409 | `type: /problems/concurrency`, no `ruleId`, detail asks the client to retry |
| `BadHttpRequestException` (body over 64 KB, too slow, truncated) | its `StatusCode` (413, 408, 400) | fixed title and detail per status, no `ruleId`, never the exception message; logged at Warning (api-contract H4) |
| request not over HTTPS, outside Development, not `/health/*` | 400 | `type: /problems/https-required`, no `ruleId` (api-contract H1) |
| rate limiter rejection | 429 + `Retry-After` | `type: /problems/rate-limited`, no `ruleId` (api-contract H5) |
| `OperationCanceledException` while `RequestAborted` is cancelled (the client went away) | none written | logged below Error (Information), no 500 problem body: nobody receives it, and it isn't a server fault (F1b, David 2026-10-18) |
| any other exception | 500 | generic `detail` + `traceId`; full exception to logs only; no stack trace outside Development |

- RFC 9457 body: `type` = `/problems/{rule-id lower-case, "/" → "-"}` (so `RENEW-1/2` → `/problems/renew-1-2`), `title`, `status`, `detail`, `instance`, `ruleId`, `traceId`, plus `RuleViolation.Extensions`. The exact payloads are owned by `api-contract`.
- Status, type and title are mapped in one place (`ProblemCatalog`), and the result is turned into a response in one place (`ResultExtensions`).
- Controllers are `[ApiController]`, `sealed`, `: ControllerBase`, `[Route("policies")]`. Public action signatures use only `Contracts` types and `ActionResult`, never Domain types.

**Strict input (unknown fields and params → 400):**
- `AddJsonOptions`: `UnmappedMemberHandling = Disallow`, `JsonStringEnumConverter(allowIntegerValues: false)`, camelCase, strict number handling. `DateOnly` serialises as `yyyy-MM-dd`.
- `RejectUnknownQueryParametersFilter` is registered globally. Any query key not bound to an action parameter (compared case-insensitively) returns 400 with `errors[key]`.
- Required DTO members are nullable and `[Required]` (e.g. `DateOnly? StartDate`), so a missing field can't bind as a silent default.
- Reference (body and route): after trim and upper-casing it must match `^[A-Z0-9-]{3,20}$`, else 400. Postcode max 8. Claim `description` max 500.
- The cancel body is optional: `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]`.

### C5 Persistence rules
- One `PoliciesDbContext`. The provider comes from `Database:Provider` (`Sqlite` | `SqlServer`), and the connection comes from `ConnectionStrings:Policies`.
- Entry point: `services.AddPersistence(configuration)`. It registers the context, `IPolicyRepository` and the `ready` health check.
- The aggregate is loaded whole (owned types or explicit includes) with no lazy loading.
- There is a unique index on `Reference`, money uses `HasPrecision(18,2)`, and enums are stored as strings.
- `Version` is configured with `.IsConcurrencyToken()`. `DbUpdateConcurrencyException` and the unique-index `DbUpdateException` are translated to `PersistenceConflictException` inside `PolicyRepository.SaveChanges`.
- Migrations are SQL Server only. They are scaffolded through `PoliciesDbContextFactory`, which always uses SQL Server.
- The app never calls `Database.Migrate()`. When the provider is `Sqlite`, it calls `EnsureCreated()` at start-up; tests do the same in the factory.
- Pre-push runs `dotnet ef migrations script --idempotent`, which must succeed.
- Queries never `OrderBy`, `Sum` or compare money in SQL, because SQLite stores `decimal` as TEXT.

### C6 Cross-cutting
- **Time:** only `UkCalendar.Today(TimeProvider)` produces "today", in Europe/London (`TimeZoneInfo.FindSystemTimeZoneById("Europe/London")`). `TimeProvider.System` is registered in Program, and tests replace it with `FakeTimeProvider`.
- **Logging:** use `ILogger` message templates only, with no string interpolation. Log the reference, rule IDs, outcome and traceId.
  - **Never log** policyholder names or dates of birth, property address lines or postcodes, or request bodies.
  - Output goes to the console locally. When `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, `AddOpenTelemetry().UseAzureMonitor()` adds App Insights. This app-side wiring is a task in a named plan node (see Open questions).
- **Health:** `/health/live` runs no checks. `/health/ready` runs the checks tagged `ready` (`AddDbContextCheck<PoliciesDbContext>`).
- **Config:**
  - `appsettings.json` holds the shape. `appsettings.Development.json` sets `Sqlite` and a SQLite file connection string, one DB per worktree.
  - Environments set `Database__Provider=SqlServer` and `ConnectionStrings__Policies` with `Authentication=Active Directory Default` (managed identity).
  - No secrets in the repo, no user-secrets and no Key Vault.
  - `DatabaseOptions` is bound with `ValidateDataAnnotations().ValidateOnStart()`.
- **Concurrency:** the app-managed `Guid Version` described above. A lost race returns 409, so two concurrent cancels can't issue two refunds.
- **OpenAPI:** `AddOpenApi()` (3.1). `MapOpenApi()` and `MapScalarApiReference()` are mapped only in Development.
- **Auth:** none, because it's out of scope. `UseAuthorization()` stays in the pipeline at its usual position.

### C7 Architecture tests (`Uinsure.Policies.Architecture.Tests`, NetArchTest.eNhancedEdition + reflection)

| # | Rule | Kind |
|---|---|---|
| A1 | Domain has no dependency on `Microsoft.*`, `System.TimeProvider`, `Uinsure.Policies.Services/Persistence/Api` | project |
| A2 | Services has no dependency on `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore*`, `Uinsure.Policies.Persistence/Api` | project |
| A3 | Persistence has no dependency on `Microsoft.AspNetCore*`, `Uinsure.Policies.Api` | project |
| A4 | No `src` assembly references `xunit*`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing` or any `*.Tests` assembly | project |
| A5 | Types in `Uinsure.Policies.Api.*` namespaces (all except `Program`) have no dependency on `Uinsure.Policies.Persistence` or `Microsoft.EntityFrameworkCore*` | namespace |
| A6 | `Api.Contracts` has no dependency on `Microsoft.AspNetCore*` (DTOs and mappings are plain C#) | namespace |
| A7 | Types in `Api.Controllers` are sealed, inherit `ControllerBase` and are named `*Controller`. Their public method parameters and return types contain no `Uinsure.Policies.Domain` type (reflection) | namespace |
| A8 | Domain entity types have no public setters (reflection) | namespace |
| A9 | No type named `*UnitOfWork*`, and no generic type named `*Repository*`, in any `src` assembly | naming |
| A10 | `PolicyRepository` is `internal sealed` | naming |
| A11 | The `RS0030` suppression appears only in `Money.cs` (a source-text check over `src/`) | source |
| A12 | Every type in `Uinsure.Policies.Persistence.Records` is `internal`, and no EF configuration (`IEntityTypeConfiguration<T>`) targets a type from `Uinsure.Policies.Domain` | namespace |
| A13 | `Policy.Restore` is called only from `Uinsure.Policies.Persistence` (source-text check over `src/`) | source |
| A14 | Domain types have no non-public parameterless constructor and no `Version` member (nothing exists only for EF) | reflection |
| A15 | `PoliciesController` depends on `IPolicyService`, not `PolicyService`; `PolicyService` implements `IPolicyService` and takes `IReferenceGenerator`; no `src` type calls `ReferenceGenerator` statically | reflection + source |

**BannedApiAnalyzers** (`BannedSymbols.txt`, applied to every `src` project via `Directory.Build.props`, `RS0030` as error):

| Banned | Use instead |
|---|---|
| `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` | `TimeProvider` → `UkCalendar.Today` |
| `TimeProvider.GetLocalNow()`, `TimeZoneInfo.Local` | `UkCalendar.Today` (server zone is UTC on Azure) |
| every `Math.Round(decimal…)` and `decimal.Round(…)` overload | `Money.Round` (`Money.cs` alone suppresses `RS0030`) |

(verify in F0) whether a doc-ID without parameters bans every overload. If it doesn't, list each overload.

### C8 Approved packages
The builder adds no other package. A new package needs David's approval recorded as an amendment to this spec.

**Central package management:** every version below is pinned once in the root `Directory.Packages.props` (`ManagePackageVersionsCentrally` = `true`). Project files carry `PackageReference` without `Version`.

| Package | Project | Version |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | Persistence | 10.0.13 |
| `Microsoft.EntityFrameworkCore.Sqlite` | Persistence, Api.Tests | 10.0.13 |
| `Microsoft.EntityFrameworkCore.Design` | Persistence (PrivateAssets=all) | 10.0.13 |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | Persistence | 10.0.x (verify in F0) |
| `Microsoft.AspNetCore.OpenApi` | Api | 10.0.x (verify in F0) |
| `Scalar.AspNetCore` | Api | 2.18.0 |
| `Azure.Monitor.OpenTelemetry.AspNetCore` | Api | (verify in F0) |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | all `src` (PrivateAssets=all) | (verify in F0) |
| `Riok.Mapperly` | Api, Persistence (ExcludeAssets=runtime, PrivateAssets=all) | 4.3.1 (amended 2026-10-18, F1b) |
| `xunit.v3` | all tests | 4.0.2 |
| `Microsoft.AspNetCore.Mvc.Testing` | Api.Tests | 10.0.x (verify in F0) |
| `Microsoft.Extensions.TimeProvider.Testing` | Services.Tests, Api.Tests | 10.11.0 |
| `NetArchTest.eNhancedEdition` | Architecture.Tests | 1.4.5 |

Not to be added: FluentValidation, MediatR, AutoMapper, Swashbuckle, Serilog, FluentAssertions, a Result/ErrorOr library, caching, domain events or outbox, Asp.Versioning, auth packages.

### C9 Agent-facing extract
*(F0a: to fit the 150-line cap, the C3 signatures sit in the skill's `signatures.md`, and api-contract's Contract in `api.md`; SKILL.md tells agents to read them.)* The contract-only `architecture` skill carries exactly the following, with no rationale, research, options or rejected-option reasoning:
1. C1 folder tree and C2 reference table, including the C2 closing rules.
2. C3 signatures, evaluation order and domain rules.
3. C4 status table, problem-body rules and strict-input rules.
4. C5 persistence rules and C6 cross-cutting rules, as bullets.
5. C7 architecture-test table and banned-API table.
6. C8 package table and the "Not to be added" list.
7. One line: "Out of scope: auth, pricing, payment processing, React frontend (`src/web/` is reserved)."

## Open questions

1. Resolved (David, 2026-10-17): no `Refund` for that term, nor for any £0 refund (COULD-2). The term's calculation is still returned with `RuleApplied` `UNPAID-TERM` and amount 0.00; the README says so (C3 domain rules).
2. Resolved in reconciliation (2026-10-17): F0b owns the App Insights wiring (planning Contract). **Which plan node owns the App Insights app wiring** (C6). Recommendation: F0b, so that cutting F5 doesn't drop it.
3. Resolved (David, 2026-10-17): yes. `Directory.Packages.props` pins C8's versions in one file (C1, C8).
4. Resolved in reconciliation (2026-10-17): requirements.md has been amended. **requirements.md was not yet amended** for these decisions:
   - D3 §3 wording (`DateOnly today` in the domain, `TimeProvider` at the service level)
   - Europe/London
   - SELL-1 format and normalisation
   - SELL-3 on 29 Feb
   - the new rules CANCEL-7 and COULD-2a–c, and RENEW-3b's 422
   - RFC 9457
