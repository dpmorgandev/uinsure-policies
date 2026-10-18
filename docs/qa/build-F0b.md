# QA script — build / F0b — Walking skeleton

Rule IDs: none (F0b has no rule IDs; each step is tagged with the acceptance item it checks, `[AC-n]`, numbered as in `docs/plans/active/build.dag.yaml`)
Branch: feature/build/F0b   Worktree: .claude/worktrees/build-F0b
Commit under test: the HEAD of feature/build/F0b when you run it (it moves with each bookkeeping commit; record the SHA in the sign-off)
API URL: http://localhost:5080   Environment: local (`tests/postman/local.postman_environment.json`)

Commands are for PowerShell (the VS 2026 Developer PowerShell or a terminal), run from the worktree root.

Acceptance items:

- AC-1: four src projects with the architecture references; a deliberate layering breach fails the architecture tests
- AC-2: health live/ready, with one WebApplicationFactory test on SQLite in-memory and FakeTimeProvider
- AC-3: initial SQL Server migration and the idempotent script; EnsureCreated for SQLite
- AC-4: banned DateTime APIs; UkCalendar across the BST/GMT change
- AC-5: a 500 with a generic detail and traceId and no stack trace
- AC-6: Azure Monitor only with APPLICATIONINSIGHTS_CONNECTION_STRING
- AC-7: OpenAPI 3.1 and Scalar in Development only
- AC-8: Postman CLI calls health
- AC-9: the "verify in F0" items recorded in the trace
- AC-10: RuleCoverageTests with every ID pending
- AC-11: the commit-on-main deny hook
- AC-12: the loop ran end to end

## Preconditions

- [ ] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: __ / __ / __). Expected: 74 / 0 / 0.
- [ ] Port 5080 is free (`netstat -ano | findstr :5080` prints nothing).
- [ ] `APPLICATIONINSIGHTS_CONNECTION_STRING` is not set in the shell or the machine (`echo $env:APPLICATIONINSIGHTS_CONNECTION_STRING` prints an empty line).
- [ ] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).

### Starting the API (pick one; both give Development on http://localhost:5080)

- **VS 2026:** open `Uinsure.Policies.slnx` and set `Uinsure.Policies.Api` as the startup project. Choose the
  **Uinsure.Policies.Api** launch profile in the green ▶ drop-down, then press ▶. The browser opens
  `http://localhost:5080/health/live`, which shows `Healthy`.
- **CLI:** `dotnet run --project src/Uinsure.Policies.Api` (it uses the same launch profile, so it runs on port
  5080 in Development). The console shows `Now listening on: http://localhost:5080` and `Hosting environment: Development`.

With either one, the SQLite file is `src/Uinsure.Policies.Api/policies.development.db`. It belongs to this
worktree and is gitignored.

## Steps

| # | Item | Postman request / test | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | [AC-2] | — | Start the API from **VS 2026** (green ▶, the `Uinsure.Policies.Api` profile). | The console shows `Now listening on: http://localhost:5080` and `Hosting environment: Development`. The browser shows `Healthy` at `/health/live`. | | |
| 2 | [AC-2] | — | Stop it. Start it again with `dotnet run --project src/Uinsure.Policies.Api`. | It shows the same two console lines. There are no errors and no OptionsValidationException. | | |
| 3 | [AC-2] | HEALTH · live → 200 | `curl.exe -i http://localhost:5080/health/live` | `200`, `Content-Type: text/plain`, body `Healthy` | | |
| 4 | [AC-2] [AC-3] | HEALTH · ready (database answers) → 200 | `curl.exe -i http://localhost:5080/health/ready` | `200`, body `Healthy`. This is the DbContext check against the SQLite file that `EnsureCreated()` made at start-up. | | |
| 5 | [AC-2] | covered by tests | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.HealthEndpointTests" --filter-class "Uinsure.Policies.Api.Tests.HealthWithUnreachableDatabaseTests"` | 4 passed. These are live/ready 200 on SQLite in-memory with `FakeTimeProvider`, ready **503** `Unhealthy` when the database can't be reached, and live still 200 in that case. | | |
| 6 | [AC-8] | folder `F0b Walking skeleton` | Leave the API running from step 2. Run `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F0b Walking skeleton"` | Both requests are `200 OK`. `Pass  HEALTH · live answers 200 Healthy` and `Pass  HEALTH · ready answers 200 Healthy when the database answers` print. The summary shows requests 2/0 failed and assertions 2/0 failed. The closing "Postman credentials are required…" line is only about cloud publishing and is not a failure. | | |
| 7 | [AC-7] | — | In a browser, open `http://localhost:5080/openapi/v1.json`. | `200` JSON with `"openapi": "3.1.1"` and the title `Uinsure.Policies.Api \| v1`. | | |
| 8 | [AC-7] | — | In a browser, open `http://localhost:5080/scalar`. | It redirects (`302` → `scalar/`) to the Scalar UI, which renders the v1 document. | | |
| 9 | [AC-7] | — | Stop the API. Start it **outside Development**: `$env:ASPNETCORE_ENVIRONMENT="Production"; $env:Database__Provider="Sqlite"; $env:ConnectionStrings__Policies="Data Source=$env:TEMP\uinsure-qa-production.db"; dotnet run --project src/Uinsure.Policies.Api --no-launch-profile --urls http://localhost:5080`. Then request `/openapi/v1.json`, `/scalar` and `/scalar/`. | All three give `404`. `/health/live` and `/health/ready` still give `200`. (The Database settings are needed here because only `appsettings.Development.json` sets them; without them start-up fails on purpose: `Database:Provider must be Sqlite or SqlServer.`) Then stop the API and run `Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:Database__Provider, Env:ConnectionStrings__Policies`. | | |
| 10 | [AC-7] | covered by tests | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.OpenApiDevelopmentTests" --filter-class "Uinsure.Policies.Api.Tests.OpenApiProductionTests"` | 4 passed | | |
| 11 | [AC-5] | covered by tests | No route throws, so this can't be done by hand. The tests add a throwing path (`/test/unhandled-exception`) through `ThrowingEndpointStartupFilter` in a Production host. Run `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.UnhandledExceptionTests"`. Read `tests/Uinsure.Policies.Api.Tests/UnhandledExceptionTests.cs`. | 3 passed. `Unhandled_exception_returns_500_problem_details_with_a_generic_detail_and_a_traceId` checks a `500` `application/problem+json` with `type`, `title`, `status`, a generic `detail`, `instance` and `traceId`, and no `ruleId`. `Unhandled_exception_response_reveals_no_exception_message_type_or_stack_trace_outside_Development` checks that the body has no sentinel message, no exception type name and no `   at ` frame. `Unhandled_exception_is_logged_with_the_exception` checks that the exception goes to the logs only. The handler is in `src/Uinsure.Policies.Api/Errors/UnexpectedExceptionHandler.cs`. | | |
| 12 | [AC-6] | — | Use the shell from the preconditions, with no `APPLICATIONINSIGHTS_CONNECTION_STRING`. Start with `dotnet run --project src/Uinsure.Policies.Api` and call `/health/live`. | It starts normally, the log goes to the console, `/health/live` gives `200`, and no Azure Monitor or OpenTelemetry exporter error appears in the console. | | |
| 13 | [AC-6] | covered by tests | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.ApplicationInsightsTests" --filter-class "Uinsure.Policies.Api.Tests.ApplicationInsightsEnabledTests"` | 4 passed. Without the setting, there's no `OpenTelemetry*` or `Azure.Monitor*` service and health gives 200. With it, both are registered and health still gives 200. Also read the one log template in `UnexpectedExceptionHandler.cs`. It holds only `{Method} {Path}`, with no PII. | | |
| 14 | [AC-4] | covered by tests | `dotnet test --project tests/Uinsure.Policies.Services.Tests` | 10 passed. These are `UkCalendar` across BST and GMT, including `2027-06-30T23:30:00Z` → `2027-07-01` (BST) and `2027-01-15T23:30:00Z` → `2027-01-15` (GMT), and both clock-change days. | | |
| 15 | [AC-4] | — | Banned API, by hand: `Set-Content src/Uinsure.Policies.Services/QaScratch.cs "namespace Uinsure.Policies.Services; internal static class QaScratch { internal static DateTime Stamp() => DateTime.Now; }"`, then `dotnet build Uinsure.Policies.slnx -warnaserror` | The build **fails** with `error RS0030` on `QaScratch.cs`: the symbol `DateTime.Now` is banned, "Use UkCalendar.Today(TimeProvider) instead." (VS also underlines it in red.) | | |
| 16 | [AC-4] | — | Remove the scratch file: `Remove-Item src/Uinsure.Policies.Services/QaScratch.cs`. Then run `git status --short` and `dotnet build Uinsure.Policies.slnx -warnaserror`. | `git status` shows nothing under `src/`, and the build succeeds with 0 warnings and 0 errors. | | |
| 17 | [AC-1] | covered by tests + red commit | Run `dotnet test --project tests/Uinsure.Policies.Architecture.Tests`. Then run `git show --stat 5f7f229`. | All Architecture tests pass at HEAD. In `5f7f229` (red), `DeliberateLayeringBreach.cs` in Domain uses `System.TimeProvider`, and the message records A1 failing on it. The breach is gone at HEAD (`src/Uinsure.Policies.Domain` has no source `.cs` files; `obj/` holds generated ones). | | |
| 18 | [AC-3] | — | `dotnet ef migrations script --idempotent --project src/Uinsure.Policies.Persistence --startup-project src/Uinsure.Policies.Api -o $env:TEMP\uinsure-migrate.sql` | It succeeds, and the file starts with `IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL`. (On this machine `dotnet-ef` is a **global** 9.0.9 tool and warns that it is older than the 10.0.13 runtime. There's no tool manifest in the repo, so pre-push's `dotnet tool restore` installs nothing. See the notes.) | | |
| 19 | [AC-10] | covered by tests | Run `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`. Open `tests/rule-coverage.pending.txt`. | 11 passed, and the file lists all 30 rule IDs. | | |
| 20 | [AC-9] | — | **At Pause 2** (the record isn't written yet): read the "verify in F0" receipts that t13 renders into `docs/trace/build/F0b/` and `walkthrough.md`. Finding A1/I5 deferred them to t13, and the reviewer accepted that in `verdicts/A1-reviewer.json`. | The receipts record xunit.v3 on Microsoft.Testing.Platform without Microsoft.NET.Test.Sdk; the Mvc.Testing, EF Core Sqlite and NetArchTest.eNhancedEdition versions; TrxReport recorded as out of scope; the BannedApi doc-IDs; and that Program needs `public partial class` (`Program.cs` ends with `public partial class Program;`). If any receipt is missing, AC-9 fails. | | |
| 21 | [AC-11] | harness self-test | `node --test ".claude/hooks/__tests__/*.test.mjs"` | `ok … G8: commit on main with the marker deny; without allow; on a feature branch allow`, and `.githooks/main-locked` exists. The summary shows `# tests 60`, `# pass 60`, `# fail 0`. | | |
| 22 | [AC-12] | — | Check the loop record: Pause 1 (t4) is done, `walkthrough.md` is rendered (t13), this script is signed below, and the PR is merged by you (t16). | Every item is present. Steps t13–t16 come after this script, so tick them at Pause 2. | | |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- Found while preparing this script: `dotnet-ef` isn't pinned in a repo tool manifest, so the version depends on the machine (step 18).
- Step 12 [AC-6]: the console can't show "no exporter error"; the on/off proof is ApplicationInsightsTests plus ApplicationInsightsEnabledTests (4 tests; the enabled one was mutation-checked by the orchestrator). Reword for F1's script.
- Steps 20 and 21: stale expectations. Step 20: the verify-in-F0 receipt is the gate.verify-in-f0 event in loop.jsonl; `public partial class Program` is not required (SDK 10.0.402); the deferral acceptance is in verdicts/A2-reviewer.json, not A1. Step 21: the self-test count is 64 after harness PRs #4 and #5.

## Sign-off

| | |
|---|---|
| Tester | David Morgan |
| Date | 2026-10-18 |
| Commit signed off | 241f3e9 (PR #6 head) |
| `dotnet test` result | 74 / 0 / 0 (total / failed / skipped; orchestrator receipt at baf1f91, later commits are trail and plan only) |
| Postman CLI result | requests 2 / failed 0; assertions 2 / failed 0 (orchestrator receipt gate.postman, fresh DB on :5080) |
| Steps passed | not counted as a total. David reviewed steps 12, 17, 20, 21 and 22 with the orchestrator at Pause 2 (all pass; notes on 12, 20, 21) and accepted F0b |
| Defects raised (finding ids) | none (QA-text notes only, see Exploratory notes) |
| Verdict | ☑ Accept F0b  ☐ Reject (findings above) |
| Signature | David Morgan, signed by Claude (tester agent) on David's explicit instruction at Pause 2, 2026-10-18 |
