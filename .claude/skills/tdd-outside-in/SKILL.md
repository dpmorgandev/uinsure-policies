---
name: tdd-outside-in
description: >-
  Outside-in TDD for one plan node: the step order, what red means, the throwing-stub rule, test
  conventions, the integration harness, Postman naming and the QA script template.
user-invocable: false
---
# Outside-in TDD

## The sequence per node (each commit carries its `TDD:` trailer)

| # | Step | Who | Content | `TDD:` |
|---|---|---|---|---|
| 0 | Scaffold (only a node that adds projects) | builder | The `.slnx`, the root build files and the `src/*/*.csproj` files; no `.cs`. Each `src/` project builds empty, so red can compile. | `scaffold` |
| 1 | Red: acceptance | tester | The **full** acceptance suite in Api.Tests: every success and failure row for the node's rules, each traited; the Postman requests; the throwing `src/` stubs needed to compile; the node's IDs removed from `tests/rule-coverage.pending.txt`. It builds, and the acceptance tests fail. | `red` |
| 2 | Stub controller | builder | Actions returning hard-coded happy-path responses (status, `Location`, body shape). Happy-path rows go green; rule-violation rows stay red. | `stub` |
| — | Pause 1 | David | Calls the stub in Postman and reads the acceptance tests. | — |
| 3 | Domain | tester → builder | Red unit tests in Domain.Tests for each rule boundary, then real domain behaviour replaces the throws. | `red`, `green` |
| 4 | Services | tester → builder | Red Services.Tests where Services has logic, then orchestration and `UkCalendar`. | `red`, `green` |
| 5 | Persistence | builder | Configurations, repository, migration. Storage-dependent acceptance rows go green. | `green` |
| 6 | Api | builder | The real controller → service call and error mapping replace the stub. **The whole acceptance suite goes green last.** | `green` |
| 7 | Refactor | builder | No behaviour change; the full suite stays green. | `refactor` |
| 8 | Review | reviewer, security | Verdicts. | — |
| 9 | QA script | tester | `docs/qa/<plan-id>-<node-id>.md`, after both PASS. | — |

**Red** means it compiles and fails for the right reason: the asserted status, body or value is wrong,
not a compile error, a missing route or a setup fault. **No test is weakened or deleted after its red
commit.** Green replaces every `NotImplementedException`; one left in `src/` fails review.

## Tester `src/` stub rule

In a tester turn, a `src/**` change may only **add** lines. No existing line is modified or deleted, and
every added line, trimmed, matches one of these:

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

Anything else (an `=` initialiser, `return`, `if`, any `new` other than `new NotImplementedException()`,
attributes, logic) is denied.

## Test conventions

- **Projects:** `tests/Uinsure.Policies.Domain.Tests` (rule logic at every boundary, `DateOnly today`
  passed in, no `FakeTimeProvider`); `.Services.Tests` (only Services logic; `UkCalendar.Today` qualifies);
  `.Api.Tests` (the acceptance suite over HTTP); `.Architecture.Tests` (layering plus the rule-coverage check).
- **Packages:** `xunit.v3` on Microsoft.Testing.Platform, plain `Assert` only.
  `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing`, and EF Core Sqlite in
  Api.Tests. Never FluentAssertions, Shouldly, AutoFixture, Bogus or mocking libraries.
- **Naming:** class = subject + `Tests` (`SellPolicyTests`, `RefundCalculatorTests`, `SellEndpointTests`).
  Method = a sentence with underscores stating the condition and the outcome:
  `Start_date_today_plus_61_is_rejected`. One behaviour per test.
- **`[Theory]`** only for genuinely equivalent inputs. Each side of a boundary is its own test.
- **Builders** live in `tests/<project>/Builders/` (`SellPolicyRequestBuilder`, `PolicyholderBuilder`,
  `PolicyBuilder`, `CancellationRequestBuilder`, `RenewalRequestBuilder`, `ClaimRequestBuilder`).
  `Build()` with no overrides gives a **valid** object relative to `TestClock.Today`. A test overrides
  only the field under test (`.WithStartDate(TestClock.Today.AddDays(61))`).
- **No random data**, and no `Guid.NewGuid()` references. Each integration test passes its own literal
  reference, for example `"SELL2-AT-61"`.
- **Clock:** `TestClock.Now = 2026-10-17T12:00:00Z`, and `TestClock.Today = 2026-10-17`. Every boundary is
  arithmetic from `TestClock.Today`. `DateTime.Now`, `UtcNow` and `Today` are banned in tests too.
- **Rule trait:** `[Trait("Rule", "<ID>")]`, a string literal, one ID per attribute, exactly as in
  `docs/requirements.md`. `RENEW-1/2` is traited as `RENEW-1` or `RENEW-2`. A test may carry several.
  Plumbing tests carry none.
  - Filter by rule: `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-2"`.
- **Integration assertions:** status first. Then, for errors, the RFC 9457 body (`type` =
  `/problems/<rule-id-lowercase>`, `ruleId`, `violations[]` for a 422, and the extension values). For
  success, assert 201 + `Location`, then read the body back via `GET`: every Sell test is a POST → GET
  round trip. The multi-violation 422 has its own test.

## Integration harness (Api.Tests)

- Use `PoliciesApiFactory : WebApplicationFactory<Program>`, one per test class (`IClassFixture`).
- In `ConfigureTestServices`:
  - Remove the app's `DbContextOptions<PoliciesDbContext>`.
  - Register SQLite on one **open** `SqliteConnection("DataSource=:memory:")` owned by the factory, and
    call `EnsureCreated()` once.
  - Register `services.AddSingleton<TimeProvider>(factory.Clock)`, a `FakeTimeProvider` at `TestClock.Now`.
- Each test class constructor resets `Clock` to `TestClock.Now`. A test may `SetUtcNow` to cross a boundary.
- **The BST boundary is required.** With the clock at `2027-06-30T23:30:00Z`, UK today is `2027-07-01`:
  - Services.Tests: `UkCalendar_today_at_23_30_UTC_in_BST_is_the_next_UK_date`.
  - Api.Tests: `Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past` (start `2027-06-30` → 422 SELL-2).
  - GMT control: `2027-01-15T23:30:00Z` → UK today `2027-01-15`.

## Rule-coverage pending list

`tests/rule-coverage.pending.txt` lists the rule IDs of nodes not yet built, one per line. In a node's red
step, the tester removes that node's IDs. An ID still pending that already has a trait fails the check.

## Postman (v2.1)

- The collection is `tests/postman/Uinsure.Policies.postman_collection.json`, plus a `local` environment
  (`baseUrl` = `http://localhost:5080`).
- Folders per node (`F1 Sell`, `F2 Retrieve`, …), each with `happy path` and `rule violations`.
- Request name: `<RULE> · <condition> → <status>`, for example `SELL-2 · start today+61 → 422`. Each
  request has at least one `pm.test` whose name starts with the rule ID.
- Requests create their own data. Pre-request scripts generate a unique `reference` and compute dates from
  the **real** today. Only real-time-runnable rows go in Postman. Rows that need a fake clock stay
  integration-only (see the real-time table in the architecture skill's `api.md`).

## QA script template (`docs/qa/<plan-id>-<node-id>.md`)

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

Exploratory notes / defects (each defect becomes a finding on this node's failure route, not a /plan-bug):
```

- There's one step per real-time row, giving the request, the expected status and `ruleId` (plus
  `violations[]` where several apply).
- Integration-only rows are listed as "covered by test `<name>`", so David can run them with `dotnet test`.
- Every node rule appears in at least one step.
