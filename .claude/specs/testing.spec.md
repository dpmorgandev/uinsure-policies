# testing.spec.md
Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Input: `docs/requirements.md` §2, §3, §4 row 4 · Agents act only on **Contract**, once it is decided.

## Dependencies

| Direction | Spec | What this spec assumes / supplies |
|---|---|---|
| Upstream (parallel, **not decided**) | `architecture.spec` | **Assumed:** projects `src/Uinsure.Policies.{Api,Application,Domain,Infrastructure}`; rules live in Domain/Application as pure code taking `today` (or `TimeProvider`); EF Core with SQLite locally; one `Money.Round` (CANCEL-6); "today" is derived from `TimeProvider` in **one** place (UTC vs Europe/London is architecture's call — see Q3). Arch-test library is chosen there. |
| Upstream (parallel, **not decided**) | `api-contract.spec` | **Assumed:** `POST /policies`, `GET /policies/{ref}`, `POST /policies/{ref}/cancellation`, `GET /policies/{ref}/cancellation-quote`, `POST /policies/{ref}/renewals`, `POST /policies/{ref}/claims`; status codes as in requirements (409/422/404); ProblemDetails body naming the rule. Route names in examples below are placeholders. |
| Downstream | `agents-skills` | tester skill = the Contract conventions below (naming, builders, traits, Postman request naming). |
| Downstream | `orchestration` | QA step uses the QA script template; red/green commit rules interact with hooks (see R7). |
| Downstream | `traceability` | Rule→test mapping via `Trait("Rule", …)` + Postman request-name prefix. |
| Downstream | `guardrails` | Git hook contents and `core.hooksPath` mechanics (R7). |
| Downstream | `deployment` | Which suites run in which pipeline stage (R9). |

## Research

### R1. What each test type proves
| Type | Proves | Does **not** prove |
|---|---|---|
| Unit (Domain/Application) | Every rule ID's logic at every boundary, with a fixed clock; rounding; date arithmetic incl. leap years. Fast (ms), run on every inner-loop step. | Wiring, HTTP status codes, persistence, serialization. |
| Integration (`WebApplicationFactory`) | Outer TDD loop: the endpoint exists, the request binds, the rule's violation surfaces as the right status + ProblemDetails, the unique index (SELL-1) fires, GET returns what POST stored. | Behaviour on Azure SQL (if SQLite is used — see R3), real Kestrel/hosting config. |
| Architecture | Layering is enforced mechanically (dependency direction, controllers don't touch `DbContext`, Domain has no EF/ASP.NET references). Makes "clear layering" a receipt, not a claim. | Correctness of any rule. |
| Postman | Executable, human-readable API documentation; the medium David uses for HITL/QA; a smoke suite reusable against a deployed environment. | Nothing integration tests don't already prove automatically — its value is **human-facing and post-deploy**, not extra coverage. |

### R2. Test framework on .NET 10 (SDK 10.0.402)
- **`dotnet test` has two modes in the .NET 10 SDK:** VSTest (default) and Microsoft.Testing.Platform (MTP), opted into with `global.json` → `{"test":{"runner":"Microsoft.Testing.Platform"}}`. In MTP mode a project that supports only VSTest **errors**; running MTP projects under VSTest mode "is considered legacy… will be removed in MTP version 2 if run with .NET 10 SDK" ([MS Learn, dotnet test, 2026-09-12](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)). Solution/project selection changes in MTP mode: `dotnet test --solution X.slnx` / `--project …`.
- **xUnit v3:** `xunit.v3` **4.0.2** (2026-10-09) on NuGet; 4.0.0 shipped 2026-08-14 with Native AOT + full parallelisation, **MTP v2 as default, MTP v1 support dropped**; release notes say if you see "VSTest isn't supported on the .NET 10 SDK", update `global.json` ([xunit 4.0.0 notes](https://xunit.net/releases/v3/4.0.0), [NuGet](https://www.nuget.org/packages/xunit.v3)). MTP filter: `--filter-trait "Rule=SELL-2"` / `--filter-not-trait` ([xunit MTP doc](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)). Test Explorer in VS uses MTP automatically for v3. `TestContext.Current` gives cancellation token etc.
- **xUnit v2** (`xunit` 2.9.x): maintenance mode, VSTest only — incompatible with MTP-mode `global.json`. Only reason to choose it is familiarity; not worth it on a greenfield .NET 10 repo.
- **MSTest** 4.5.1 (2026-10-07, NuGet): Microsoft-owned, MTP-native, fine — but requirements §3 Stack names xUnit, and xUnit is the more common ASP.NET Core choice (MS Learn integration-test samples use it).
- **TUnit** 1.75.0 (2026-10-13): source-generated, built on MTP, fast and modern. Less common in interviews; a novel framework costs explanation time without proving anything the brief asks for.
- **Uncertainty:** I did not verify that `TngTech.ArchUnitNET.xUnitV3` targets xunit.v3 **4.x** (it may pin 3.x extensibility APIs that 4.0 broke). Mitigation: ArchUnitNET's core assertion works without the xUnit adapter (assert on `rule.Evaluate(arch)` / `HasNoViolations`) — verify in F0.

### R3. Integration-test database
EF Core team guidance ([MS Learn, Choosing a testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)): test against the production DB system where possible; if a fake is needed, SQLite in-memory over the InMemory provider; **the InMemory provider is "highly discouraged… only supported for legacy applications"** (no transactions, no relational constraints → it would not enforce SELL-1's unique index at all). ASP.NET Core 10 integration-test docs replace the `DbContext` in `ConfigureWebHost`/`ConfigureServices` with an **open** in-memory SQLite connection ([MS Learn, integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)).

| Option | Pros | Cons |
|---|---|---|
| **SQLite in-memory** (connection held open per factory) | Same provider as local dev; ms startup; isolated per factory; unique index enforced; zero infra. | Not Azure SQL: case-sensitive by default, `decimal` stored as TEXT (EF can't translate decimal `ORDER BY`/aggregates on SQLite), `DateOnly` as TEXT; **unique-violation exception differs** (SQLite error 19 vs SQL Server 2601/2627) — the SELL-1 409 mapping is provider-specific code that SQLite tests cannot prove for prod. |
| SQLite file | Can inspect the DB after a failing run. | File cleanup, parallel-test contention, same provider gaps. No advantage over in-memory for automated tests (useful for the HITL run — that is architecture/orchestration's concern). |
| **Testcontainers SQL Server** (`Testcontainers.MsSql`) | Real SQL Server engine ≈ Azure SQL behaviour; proves migrations and the 2601/2627 → 409 mapping; "their stack beats our preferences". Docker 29.1.3 is installed here. | Needs Docker Desktop running (hooks fail otherwise); first pull of the image is ~GBs; ~10–20 s container start per run (estimate, not measured); slower inner loop. Package version not verified. |
| EF InMemory | Trivial setup. | Discouraged by Microsoft; doesn't enforce the unique index → SELL-1 untestable. **Rejected.** |

### R4. Assertions
- **Plain `Assert`** (xunit.v3.assert): zero extra dependency; v4 adds `Assert.All(…, strict)`; `Assert.Equivalent` covers DTO comparison. Already chosen in requirements §3.
- **FluentAssertions v8+** — verified commercial: licence moved to the Xceed Community License (non-commercial only; paid licence otherwise); v7 stays Apache 2.0 ([DevClass, 2025-01-16](https://devclass.com/2025/01/16/another-open-source-project-shifts-to-restrictive-license-fluent-assertions-following-xceed-partnership); Xceed FAQ). Rejected — a licence question in an interview is noise.
- **AwesomeAssertions** — Apache 2.0 community fork of FA ([NuGet mirror 8.2.0](https://nuget.rza.ru/packages/awesomeassertions/8.2.0)). **Shouldly** 4.3.0 (Jan 2025; 5.0.0-preview.2 Jul 2026), BSD-3. Both fine, both add a dependency and a DSL to explain. Not needed.

### R5. FakeTimeProvider
`Microsoft.Extensions.TimeProvider.Testing` **10.11.0** (2026-10-14), ships net8/9/10 ([NuGet](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing)). API: `new FakeTimeProvider(DateTimeOffset start)`, `SetUtcNow`, `Advance(TimeSpan)`, `SetLocalTimeZone(TimeZoneInfo)`, `AutoAdvanceAmount`. Unit tests: pass it (or `today` derived from it) into the rule. Integration tests: `services.AddSingleton<TimeProvider>(fake)` in `ConfigureTestServices`, so "today" is deterministic over HTTP too. A Roslyn ban on `DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow`, `DateOnly.FromDateTime(DateTime.Now)` (`Microsoft.CodeAnalysis.BannedApiAnalyzers`) makes injection mechanical — suggest to architecture/guardrails (not verified for .NET 10 version).

### R6. Architecture-test libraries (briefly — choice belongs to architecture.spec)
- **NetArchTest.Rules** 1.3.2 — last release **2021-05-23**; netstandard2.0 so it loads on net10, but unmaintained 5 years ([NuGet](https://www.nuget.org/packages/NetArchTest.Rules)). Fluent, tiny, type-level dependency checks only.
- **TngTech.ArchUnitNET** 0.13.4 (2026-08-20), active; cycle detection, member-level rules, `TngTech.ArchUnitNET.xUnitV3` adapter ([NuGet](https://www.nuget.org/packages/TngTech.ArchUnitNET)). Slightly heavier API (load `Architecture` once, static field). Lean: ArchUnitNET (maintained beats lighter).

### R7. Git hooks
- **Key conflict found:** TDD must be visible in history → **red commits contain a failing test**. A pre-commit hook that runs tests would block every red commit. Also, C# red tests that reference non-existent types don't compile; if pre-commit enforces build, the red step must compile against a stub (e.g. method throwing `NotImplementedException`, or the outer loop's hard-coded controller from §3 step 2). That is a TDD-visible convention, not a weakness: "red = compiles, asserts fail".
- Therefore: **pre-commit = `dotnet build -warnaserror`** (fast, keeps every commit compilable, matches the proven Stop hook). **pre-push = build + full `dotnet test`** (unit + arch + integration must be green; pushes happen at integration time, when everything is green).
- Versioning: commit hooks in `.githooks/`, enable once with `git config core.hooksPath .githooks`. A relative `core.hooksPath` is resolved relative to where hooks run, i.e. the working-tree root ([git-config](https://git-scm.com/docs/git-config#Documentation/git-config.txt-corehooksPath), [githooks](https://git-scm.com/docs/githooks)) — so each worktree (`../wt/F1-sell`) runs **its own branch's** hook files, and the config (shared across worktrees) needs setting once. Windows: Git for Windows runs hooks via its bundled `sh`, so `#!/bin/sh` scripts work from VS, Git Bash and PowerShell; keep them **LF** (`.gitattributes`: `.githooks/* text eol=lf`, already learned for `*.sh`) and mark executable in the index: `git update-index --chmod=+x .githooks/pre-push`. Hooks are bypassable with `--no-verify` — guardrails must deny that for agents.
- **Postman in pre-push — sketch:**
  ```sh
  dotnet build -c Release -warnaserror || exit 1
  dotnet test -c Release --no-build || exit 1
  db="$(mktemp -u).db"; port=5199
  ConnectionStrings__Policies="Data Source=$db" ASPNETCORE_URLS="http://127.0.0.1:$port" \
    dotnet src/Uinsure.Policies.Api/bin/Release/net10.0/Uinsure.Policies.Api.dll & pid=$!
  for i in $(seq 30); do curl -sf "http://127.0.0.1:$port/health" >/dev/null && break; sleep 1; done
  postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
    -e tests/postman/local.postman_environment.json --env-var "baseUrl=http://127.0.0.1:$port" --bail
  rc=$?; kill $pid; rm -f "$db"; exit $rc
  ```
  Run the built DLL, not `dotnet run` (`dotnet run` spawns a child; `kill` in Git Bash may orphan it and hold the port). Needs: a health endpoint, a fresh SQLite file, a fixed port not clashing with David's HITL run, Postman CLI installed (**not installed on this machine** — checked), and a collection that creates its own data (`{{$guid}}` refs). **Cost (estimate, not measured):** +5–15 s per push plus a new failure mode (port in use, orphaned process, CLI missing). Coverage gained over integration tests: ~nil, because they hit the same endpoints in-process.

### R8. Postman
- **Formats:** v2.1 = one JSON file. v3 (Postman v12+, "Native Git") = YAML tree (`*.request.yaml`, `.resources/`, `definition.yaml`, `.postman/resources.yaml`); `postman collection migrate` converts 2.1 → 3 ([collections schemas](https://learning.postman.com/docs/collections/collections-schemas)). **Verified: Newman can't run v3**, and Newman is no longer maintained; Postman says migrate to the Postman CLI ([Newman doc](https://learning.postman.com/docs/collections/using-newman-cli/command-line-integration-with-newman/)). This updates requirements §5 wording: the limit is the **v3 format**, not "v12+ collections" as such — v12 can still export 2.1, which Newman runs.
- **Postman CLI** `postman collection run <file|id>`: `-e/--environment <file|UID>`, `--env-var k=v`, `--bail [folder|failure]`, `-r cli|json|junit|html`, timeouts. **For v3 YAML collections only the `cli` reporter is supported** — no JUnit file ([Postman CLI collections](https://learning.postman.com/docs/postman-cli/postman-cli-collections/)). Local-file runs appear not to need login (docs only require sign-in for ID runs/uploading results) — **verify in F0**.
- Environment files: `local.postman_environment.json` (`baseUrl`); `dev`/`test` envs only if deployment.spec wants post-deploy smoke. Never store secrets (none exist — no auth).
- Test scripts: `pm.test("SELL-2 rejects start = today+61 → 422", () => pm.response.to.have.status(422))`, plus assert ProblemDetails `type`/`title` names the rule. Dates computed in a pre-request script relative to `new Date()` (real clock; the API runs on the real `TimeProvider` during HITL).

### R9. Boundary-value technique (fixed clock, e.g. today = 2026-10-17)
Test **at, just inside and just outside** every edge; inclusive edges get both sides.
| Rule | Fail | Pass | Pass | Fail |
|---|---|---|---|---|
| SELL-2 start | today−1 | today | today+60 | today+61 |
| SELL-3 end | — | 2026-10-17 → 2027-10-16 | leap: start in Jan 2028 → term spans 29 Feb (366 days); start 2028-02-29 → ? (**Q1**) | — |
| SELL-4 holders | 0 | 1 | 3 | 4 |
| SELL-5 age | 16th birthday = start+1 | 16th birthday = start | (leapling DOB 29 Feb — **Q2**) | — |
| CANCEL-1a/b/c | — | cancel start−1 (1a full) · start (1b full) · start+14 (1b full) | start+15 → pro-rata (1c) | — |
| CANCEL-1c | — | cancel on end date → 0 unused → £0 | 365- and 366-day terms | — |
| CANCEL-3 | today−1 | today · omitted→today | — | — |
| CANCEL-6 | — | `Money.Round(0.125m) == 0.13m` (banker's gives 0.12) — test the function directly; plus one end-to-end refund that rounds | — | — |
| RENEW-1/2 | end−31 | end−30 | end | end+1 |
| COULD-2 | — | claim + cancel before start → £0 (overrides 1a/1b) | — | — |
Also a "today" boundary at the clock: `FakeTimeProvider` at 23:30 UTC in BST, if architecture defines today as UK-local (**Q3**).

## Options

| # | Decision | Options | Lean |
|---|---|---|---|
| O1 | Framework | xUnit v3 (4.x) on MTP · xUnit v2 (VSTest) · MSTest 4 · TUnit | **xUnit v3 + MTP `global.json`** |
| O2 | Assertions | plain Assert · Shouldly · AwesomeAssertions · FA v8 (commercial) | **plain Assert** (already in reqs) |
| O3 | Integration DB | SQLite in-memory · SQLite file · Testcontainers SQL Server · (EF InMemory rejected) | **SQLite in-memory**; Testcontainers = David's call (O3b) |
| O3b | Add a small SQL Server suite? | none · one `[Trait("Db","SqlServer")]` class proving SELL-1 + migrations, excluded from hooks | lean **defer**; adopt only if architecture's duplicate-key mapping is provider-specific (it will be) and time allows |
| O4 | Naming | `Method_State_Expected` · Given/When/Then · sentence-with-underscores | **sentence**: `Start_date_61_days_ahead_is_rejected` |
| O5 | Test data | builders with valid defaults · Object Mother · AutoFixture/Bogus | **builders**; no random data (hurts boundaries and explainability) |
| O6 | Rule mapping | `[Trait("Rule","SELL-2")]` literal · custom `[Rule]` trait attribute · const class | **literal Trait** (greppable without a build; `--filter-trait` works) |
| O7 | Postman format | v2.1 JSON · v3 YAML | **v2.1** (one file, JUnit reporter for receipts/ADO, Newman fallback); v3 is Postman's direction and diffs better — close call |
| O8 | Postman in pre-push | yes · no · opt-in (`RUN_POSTMAN=1`) | **no** (R7 cost vs ~nil coverage); keep the sketch as `tests/postman/run.sh` for QA/deployment |
| O9 | Hooks | tests in pre-commit · build pre-commit + tests pre-push | **build pre-commit, tests pre-push** (R7 conflict) |
| — | Considered, cut (over-building guard) | Stryker mutation testing, coverage thresholds, snapshot testing (Verify), Bogus | cut: none used by F0/F1 |

## Recommendation
1. xUnit v3 (`xunit.v3` 4.0.x) + `global.json` MTP runner; three projects: `tests/Uinsure.Policies.UnitTests`, `.IntegrationTests`, `.ArchitectureTests`; `tests/postman/`. Shared `Directory.Build.props` for test packages.
2. Plain `Assert`. `FakeTimeProvider` everywhere time matters; a `TestClock.Today = 2026-10-17` constant so every boundary reads as arithmetic from one date.
3. Integration tests on SQLite in-memory via a custom `PoliciesApiFactory : WebApplicationFactory<Program>` (open connection, `EnsureCreated`/migrate, swap `TimeProvider`). One factory per test class (`IClassFixture`), unique references per test. If `Program` isn't visible, add `public partial class Program;` (whether .NET 10 generates this was **not verified**).
4. Every test that proves a rule carries `[Trait("Rule","<ID>")]`; integration tests also carry `[Trait("Type","Integration")]` (unit/arch distinguished by project). A test may carry several rule traits. Traceability greps `Trait("Rule", "…")` and can run `dotnet test -- --filter-trait "Rule=SELL-2"`.
5. Hooks: pre-commit build `-warnaserror`; pre-push build + `dotnet test`. Red commits must compile. Postman stays out of hooks; it is run by David at HITL and (optionally) by deployment as post-deploy smoke.
6. Postman v2.1 collection + `local` environment, run with the Postman CLI (to install in F0). Request names prefixed with rule ID.
7. Pipeline (for deployment.spec): build stage runs unit + arch + integration (`Microsoft.Testing.Extensions.TrxReport` for `PublishTestResults`); post-deploy stage runs Postman `-r junit` against `dev`.

## Decision — DECIDED 2026-10-17 (David)
- [x] O1–O8 accepted as a block. O3b (SQL Server Testcontainers) is deferred because it needs Docker. O8: Postman isn't in any hook, so it's manual only, which closes requirements §6 item 1.
- [x] **O9 / red commits: the tester adds throwing stubs.**
  - A red commit compiles and fails. The tester may add to `src/` only member *signatures* whose bodies are `throw new NotImplementedException()`.
  - A PreToolUse hook enforces this. Any `src/` line the tester adds must be a signature, a brace or that throw (the mechanism is in guardrails).
  - Green replaces the throws. Pre-commit builds, and pre-push builds and runs the tests.
- [x] **Q1–Q4 resolved upstream:**
  - SELL-3 on 29 Feb → 28 Feb.
  - The SELL-5 leapling case is moot.
  - Europe/London, so a 23:30 UTC BST boundary test exists.
  - CANCEL-7 for dates after the end.
  - A CANCEL-5 future term gets a full refund, per requirements.
- [x] **Reconcile:** the architecture-test library is **NetArchTest.eNhancedEdition**, not ArchUnitNET (architecture D8), so drop the ArchUnitNET xUnitV3 check from Q6.
- [x] **Final rulings (David, 2026-10-17):**
  - Postman receipt before Pause 2: the orchestrator runs the node's real-time-runnable folder via the Postman CLI against port 5080 and records pass/fail in `loop.jsonl` (orchestration). David still steps through manually; Postman is still in no git hook. It depends on the Q6 F0a check (local run without login), else David-only.
  - Central package management (architecture): test package versions are pinned in the root `Directory.Packages.props`.
  - Accepted as proposed: the stub-controller commit carries `TDD: stub`; `tests/rule-coverage.pending.txt` is empty after F4; the tester creates the `tests/*.csproj` files.

## Contract

Binding. Where Research, Options or Recommendation above say otherwise (project names, ArchUnitNET, Postman in pipeline), this section wins.

### C1. Test projects
| Project | References | Holds |
|---|---|---|
| `tests/Uinsure.Policies.Domain.Tests` | Domain | Rule logic at every boundary (`Policy`, `RefundCalculator`, `Money.Round`), `DateOnly today` passed in. No `FakeTimeProvider`. |
| `tests/Uinsure.Policies.Services.Tests` | Services, Domain | Only if a Services class has logic beyond orchestration. `UkCalendar.Today` qualifies, so it is expected from F0b. |
| `tests/Uinsure.Policies.Api.Tests` | Api, Persistence | Integration / acceptance suite over HTTP (C5). |
| `tests/Uinsure.Policies.Architecture.Tests` | all four src projects | Layering rules (owned by architecture.spec) via NetArchTest.eNhancedEdition, plus the rule-coverage check (C4). |
| `tests/postman/` | — | `Uinsure.Policies.postman_collection.json` (v2.1) + `local.postman_environment.json` (C9). |

Nothing in `tests/` references `.claude/`. Test package versions are pinned once, in the root `Directory.Packages.props` (central package management, architecture C8); `tests/Directory.Build.props` holds shared test settings only. The tester creates the `tests/*/*.csproj` files.

### C2. Packages
- `xunit.v3` 4.0.x on Microsoft.Testing.Platform; `global.json` → `"test": {"runner": "Microsoft.Testing.Platform"}`. No `Microsoft.NET.Test.Sdk` (verify in F0). Plain `Assert` only.
- `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 (Services.Tests, Api.Tests).
- `Microsoft.AspNetCore.Mvc.Testing` 10.0.x (verify in F0).
- `Microsoft.EntityFrameworkCore.Sqlite`, same version as Persistence's EF Core (verify in F0).
- `NetArchTest.eNhancedEdition` (version verify in F0).
- `Microsoft.Testing.Extensions.TrxReport` (pipeline test results; version verify in F0).
- `dotnet-ef` as a local tool in `.config/dotnet-tools.json` (pre-push, C8).
- Not used: FluentAssertions, Shouldly, AwesomeAssertions, AutoFixture, Bogus, Testcontainers (deferred), Moq-style mocking libraries.

### C3. Conventions
- **Naming.** Class = subject + `Tests` (`SellPolicyTests`, `RefundCalculatorTests`, `SellEndpointTests`). Method = a sentence with underscores stating condition and outcome: `Start_date_today_plus_61_is_rejected`. One behaviour per test. `[Theory]` only for genuinely equivalent inputs; each boundary side is its own test.
- **Builders.** `tests/<project>/Builders/` (`SellPolicyRequestBuilder`, `PolicyholderBuilder`, `PolicyBuilder`, `CancellationRequestBuilder`, `RenewalRequestBuilder`, `ClaimRequestBuilder`). `Build()` with no overrides yields a **valid** object relative to `TestClock.Today`; a test overrides only the field under test (`.WithStartDate(TestClock.Today.AddDays(61))`). No random data, no `Guid.NewGuid()` references: each integration test passes its own literal reference (e.g. `"SELL2-AT-61"`).
- **Clock.** `TestClock.Now = 2026-10-17T12:00:00Z`, `TestClock.Today = 2026-10-17` (same date in UTC and Europe/London). Every boundary reads as arithmetic from `TestClock.Today`. `DateTime.Now/UtcNow/Today` are banned (BannedApiAnalyzers) in tests too.
- **Rule trait.** Every test that proves a rule carries `[Trait("Rule", "<ID>")]`: a string literal, exactly one ID per attribute, the ID exactly as in `docs/requirements.md` (compound row `RENEW-1/2` is traited as `RENEW-1` or `RENEW-2`). A test may carry several. Plumbing tests carry none. Filter: `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-2"`.
- **Assertions (integration).** Status first; then for errors the RFC 9457 body: `type` = `/problems/<rule-id-lowercase>`, `ruleId`, and for 422 the `violations[]` list; for success the body, read back via `GET` (every Sell test is a POST → GET round trip).

### C4. Rule-coverage check
`RuleCoverageTests` in Architecture.Tests, so it runs in every `dotnet test` (pre-push, pipeline). Computed from source text, no build of other test projects needed:
1. **Universe U** = IDs in the first column of the rule tables in `docs/requirements.md`, matched by `^\|\s*((SELL|GET|CANCEL|RENEW|COULD)-\d+[a-z]?(/\d+)?)\s*\|`; `X-1/2` expands to `X-1`, `X-2`. U must include SELL-7, CANCEL-7, COULD-2, COULD-2a, COULD-2b, COULD-2c, RENEW-3a and RENEW-3b; the test fails if any of these is missing from requirements.md.
2. **Traits T** = every literal matched by `\[Trait\("Rule",\s*"([^"]+)"\)\]` in `tests/**/*.cs`, keyed by project.
3. **Pending P** = IDs listed in `tests/rule-coverage.pending.txt` (one per line): rules of plan nodes not yet built. The tester removes a node's IDs in that node's red step. The file is empty when F4 is done.
4. Fails, listing offenders, when: an ID in U∖P has no trait in `Domain.Tests`/`Services.Tests` (unit) **or** none in `Api.Tests` (integration); a trait in T is not in U (typo guard); an ID in P already has a trait (stale pending).

### C5. Integration harness
- `PoliciesApiFactory : WebApplicationFactory<Program>`, one per test class (`IClassFixture`). If `Program` isn't visible, add `public partial class Program;` (verify in F0 whether .NET 10 generates it).
- `ConfigureTestServices`: remove the app's `DbContextOptions<PoliciesDbContext>`, register SQLite on one **open** `SqliteConnection("DataSource=:memory:")` owned by the factory, `EnsureCreated()` once; register `services.AddSingleton<TimeProvider>(factory.Clock)` where `Clock` is a `FakeTimeProvider` at `TestClock.Now`.
- Each test class constructor resets `Clock` to `TestClock.Now`; a test may `SetUtcNow` to cross a boundary. HTTP via `factory.CreateClient()`.
- **BST boundary (required):** with `Clock` at `2027-06-30T23:30:00Z` (00:30 BST on 1 Jul), UK today is `2027-07-01`. Services.Tests: `UkCalendar_today_at_23_30_UTC_in_BST_is_the_next_UK_date`. Api.Tests: `Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past` (start `2027-06-30` → 422 SELL-2). Plus the GMT control: `2027-01-15T23:30:00Z` → UK today `2027-01-15`.
- SQLite cannot prove SQL Server-specific behaviour (unique-violation codes, migrations); the pre-push idempotent script (C8) proves migrations generate; the rest is a README-stated gap.

### C6. Outside-in TDD sequence (per feature node)
| # | Step | Who | Content | Commit `TDD:` |
|---|---|---|---|---|
| 1 | Red: acceptance | tester | The **full** acceptance suite in Api.Tests: every success and failure row for the node's rules, each traited; the Postman requests; the throwing `src/` stubs needed to compile (C7); node IDs removed from `rule-coverage.pending.txt`. Builds; acceptance tests fail. | `red` |
| 2 | Stub controller | builder | Controller action(s) returning hard-coded happy-path responses (status, `Location`, body shape). Happy-path rows go green; rule-violation rows stay red. | `stub` |
| — | **Pause 1** | David | Calls the stub in Postman; reads the acceptance tests. Loop waits. | — |
| 3 | Domain | tester → builder | Red unit tests in Domain.Tests for each rule boundary (`red`), then real domain behaviour replaces the throws (`green`). | `red`, `green` |
| 4 | Services | tester → builder | Red Services.Tests where Services has logic (`red`), then orchestration + `UkCalendar` (`green`). | `red`, `green` |
| 5 | Persistence | builder | Configurations, repository, migration. Acceptance rows depending on storage go green. | `green` |
| 6 | Api | builder | Stub replaced by the real controller → service call, error mapping. **The whole acceptance suite goes green last.** | `green` |
| 7 | Refactor | builder | No behaviour change; full suite stays green. | `refactor` |
| 8 | Review | reviewer, security | Verdicts. On FAIL: the failure route (orchestration.spec). | — |
| 9 | QA script | tester | `docs/qa/<plan-id>-<node-id>.md` from C10, after both PASS. | — |
| — | **Pause 2** | orchestrator, David | The orchestrator first records the `gate.postman` receipt (C9). Then HITL sign-off with `walkthrough.md`, the QA script and Postman. | — |

No test is weakened or deleted after its red commit (reviewer checks).

### C7. Tester `src/` stub rule (hook-checkable)
In a tester turn, a change under `src/**` is allowed only if it **adds** lines (no existing `src/` line is modified or deleted) and every added line, trimmed, matches one of:

```text
blank            ^$
brace            ^[{}];?$
using            ^using [\w.]+;$
namespace        ^namespace [\w.]+;$
type header      ^(public|internal)( (sealed|static|abstract|partial))* (class|record|struct|interface|enum) \w+(<[^>]+>)?(\(.*\))?( : [\w.<>, ]+)?;?$
member signature ^(public|internal|protected)( (static|virtual|override|abstract|async))* [\w.<>?\[\], ]+ \w+(<[^>]+>)?\([^)]*\)( => throw new NotImplementedException\(\);)?$
auto-property    ^(public|internal) [\w.<>?\[\], ]+ \w+ \{ get; (init; |set; )?\}$
enum member      ^\w+,?$
throw            ^throw new NotImplementedException\(\);$
```

Any other line (an `=` initialiser, `return`, `if`, any `new` other than `new NotImplementedException()`, attributes, logic) denies the edit. Green replaces every throw; a remaining `NotImplementedException` in `src/` at step 7 fails review. The PreToolUse mechanism is owned by guardrails.spec.

### C8. Git hooks
`.githooks/` (sh, LF via `.gitattributes`, executable bit in the index), enabled with `git config core.hooksPath .githooks`. `--no-verify` and hook-path overrides are denied to everyone (guardrails G11).
- **pre-commit:** `dotnet build Uinsure.Policies.slnx -warnaserror`. Red commits pass because they compile.
- **pre-push:** `dotnet build -c Release -warnaserror` → `dotnet test --solution Uinsure.Policies.slnx -c Release --no-build` → `dotnet tool restore && dotnet ef migrations script --idempotent --project src/Uinsure.Policies.Persistence --startup-project src/Uinsure.Policies.Api -o "$TMPDIR/migrate.sql"`. Any non-zero exit blocks the push; the script is not committed.
- Postman is in no hook.

### C9. Postman
- v2.1 collection + `local` environment (`baseUrl` = `http://localhost:5080`, the HITL port). No secrets exist.
- Run **manually** by David with the Postman CLI: `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json`. Not in hooks, not in the pipeline (the pipeline smoke is `curl /health/ready`).
- **Receipt before Pause 2:** the orchestrator runs the same command with `-i "<node folder>"` against the worktree API on port 5080 and records pass/fail as `gate.postman` in `loop.jsonl` (orchestration). The node folder holds only real-time-runnable requests (api-contract's real-time column). It depends on the F0a check in Q6 (a local collection runs without login); if that fails, Postman is David-only. David still steps through manually.
- Folders per node (`F1 Sell`, `F2 Retrieve`, …), each with `happy path` and `rule violations`. Request name: `<RULE> · <condition> → <status>`, e.g. `SELL-2 · start today+61 → 422`. Each request has ≥1 `pm.test` whose name starts with the rule ID.
- Requests create their own data; dates come from pre-request scripts on the real clock (the HITL API runs on the real `TimeProvider`).

### C10. QA script template — `docs/qa/<plan-id>-<node-id>.md`
```markdown
# QA script — <plan-id> / <node-id> — <feature name>
Rule IDs: SELL-1, SELL-2, …     Branch: feature/<plan-id>/<node-id>   Worktree: .claude/worktrees/<plan-id>-<node-id>
Commit under test: <sha>        API URL: http://localhost:5080        Environment: local
Preconditions:
- [ ] `dotnet test` green in the worktree (passed / failed / skipped: __ / __ / __)
- [ ] API started from the worktree with its own SQLite file: <command>
- [ ] Postman collection + `local` environment from this branch

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | SELL-2 | SELL-2 · start today+61 → 422 | Send; inspect body | 422; `type` /problems/sell-2; `ruleId` SELL-2 | | |
| 2 | … | | | | | |

Exploratory notes / defects (each defect becomes a finding on this node's failure route, not a /plan-bug):

## Sign-off
Result: ☐ PASS ☐ FAIL       Steps passed: __ / __
Signed by: David Morgan      Date (real clock): ____-__-__
Notes:
```

## Open questions
- ~~Q1 SELL-3 leap day~~ resolved: 29 Feb start → 28 Feb end, named test.
- ~~Q2 SELL-5 leapling~~ resolved: moot.
- ~~Q3 today~~ resolved: Europe/London; BST boundary test in C5.
- ~~Q4 after-end / CANCEL-5~~ resolved: CANCEL-7 (422); CANCEL-5 future term gets a full refund.
- **Q5 — requirements.md edits.** Resolved in reconciliation (2026-10-17): applied to §3, §5 and §6. §3 "Local git hooks run the build and tests" → "pre-commit builds; pre-push builds, tests and generates the idempotent migration script"; §5 Postman → "Newman can't run the v3 format and is unmaintained; use the Postman CLI"; §6 item 1 → closed "no" (manual only); add rule rows CANCEL-7, COULD-2a, COULD-2b, COULD-2c (C4 fails until they exist).
- **Q6 (open, verify in F0):** xunit.v3 4.x on MTP without `Microsoft.NET.Test.Sdk`; NetArchTest.eNhancedEdition version and loading on net10; `Microsoft.AspNetCore.Mvc.Testing`/EF SQLite/TrxReport versions; whether `public partial class Program` is still needed; Postman CLI install and local-file run without login; `dotnet ef migrations script --idempotent` exits 0 before the first migration exists.
