# QA script — build / F6 — README

Rule IDs: none (F6 is a docs node; the step rows carry `—` in the Rule column and name the acceptance item they prove)
Branch: feature/build/F6   Worktree: .claude/worktrees/build-F6
Commit under test: c841ae4 (HEAD when this script was run. The docs were last changed at 46b175e, where the A4 reviewer and security verdicts are PASS; c841ae4 changes only `docs/plans/active/build.dag.yaml`. This script's own commit changes only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080   Environment: local (`http` launch profile, Development, SQLite file)

F6 rewrites the project `README.md` as a short answer to the assessor (what it is, how to run it, the assumptions
taken), moves the reference material to `docs/design.md` and `infrastructure/README.md`, and ticks
`docs/readme-checklist.md`. F6 changes nothing in `src/` or `tests/` (step 57). The steps below follow the README
exactly as written, open every link, compare each assumption with its requirements row, and check the checklist.

## Acceptance items (from `docs/plans/active/build.dag.yaml`, node F6)

| Tag | Acceptance item |
|---|---|
| AC1 | `README.md` is short: what it is, how to run it (API, tests, Postman) and the assumptions taken where the brief was unclear, each in a line. Reference material lives in `docs/design.md` and `infrastructure/README.md`, which the README links to |
| AC2 | Every item in `docs/readme-checklist.md` is ticked and links to the README, `docs/design.md` or `infrastructure/README.md` section that satisfies it; F5's items say "cut" if F5 was cut |

## Preconditions

- [x] .NET SDK on `PATH`. Run with: 10.0.402 (`global.json` pins 10.0.100, `rollForward: latestFeature`).
- [x] Postman CLI on `PATH`. Run with: 1.75.0. No login is needed for a local run.
- [x] Nothing listening on port 5080 before step 2 (`netstat -ano`, no match).
- [ ] **Clean SQLite file:** delete `src/Uinsure.Policies.Api/policies.development.db*` before step 2. **Not done in
      this run** (note 1). The run used the existing, git-ignored files (`.gitignore` lines 7 and 9).

Commands are for PowerShell or Git Bash, run from the worktree root. Steps 1, 2, 7 and 8 are the README's commands
copied verbatim (README lines 17, 18, 28, 37-38).

## Steps

### A. "How to run it", followed as written (AC1)

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | — | AC1 · build | `dotnet build Uinsure.Policies.slnx -warnaserror` | `Build succeeded.`, 0 warnings, 0 errors, exit 0 | `Build succeeded.` `0 Warning(s)` `0 Error(s)`, exit 0 | PASS |
| 2 | — | AC1 · run the API | `dotnet run --project src/Uinsure.Policies.Api --launch-profile http` (leave it running) | `Now listening on: http://localhost:5080`, `Hosting environment: Development`, with nothing to configure | `Using launch settings from src\Uinsure.Policies.Api\Properties\launchSettings.json...`, then `Now listening on: http://localhost:5080`, `Application started`, `Hosting environment: Development`. The only SQL logged was EnsureCreated's `SELECT COUNT(*) FROM "sqlite_master" …`, because the file already existed (note 1) | PASS |
| 3 | — | AC1 · liveness | `curl -i http://localhost:5080/health/live` | `200`, body `Healthy` | `HTTP/1.1 200 OK`, `Content-Type: text/plain`, body `Healthy` | PASS |
| 4 | — | AC1 · readiness (the database) | `curl -i http://localhost:5080/health/ready` | `200`, body `Healthy` | `HTTP/1.1 200 OK`, `Content-Type: text/plain`, body `Healthy` | PASS |
| 5 | — | AC1 · OpenAPI document (Development only) | `curl -s http://localhost:5080/openapi/v1.json`; read `openapi`, `info.title` and the `paths` keys | `200`, JSON, OpenAPI 3.1, the policy routes | `200 application/json;charset=utf-8`, 25,426 bytes. `openapi` `3.1.1`, title `Uinsure.Policies.Api \| v1`, six paths: `/policies`, `/policies/{reference}`, `/policies/{reference}/cancellation`, `/policies/{reference}/cancellation-quote`, `/policies/{reference}/claims`, `/policies/{reference}/renewals` | PASS |
| 6 | — | AC1 · Scalar UI (Development only) | Open `http://localhost:5080/scalar` (or `curl -L`) | The Scalar page loads | `302` to `http://localhost:5080/scalar/`, which answers `200 text/html` | PASS |
| 7 | — | AC1 · run the tests | `dotnet test --solution Uinsure.Policies.slnx` (the API from step 2 still running) | The figures in README lines 31-32: 637 tests (173 domain, 65 services, 332 API acceptance, 67 architecture), 0 failed, 0 skipped | `total: 637`, `failed: 0`, `succeeded: 637`, `skipped: 0`; all four test projects passed. Run one by one (each project's test executable), the per-project totals are Domain 173, Services 65, Api 332, Architecture 67, each with 0 failed and 0 skipped. No file lock from the running API | PASS |
| 8 | — | AC1 · run the Postman collection | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json` (the API still running) | README line 34: 109 requests. Every assertion passes | Summary: iterations 1 / 0 failed; requests 253 / 0; test-scripts 109 / 0; prerequest-scripts 114 / 0; assertions 246 / 0; exit 0. The collection holds 109 request items in six folders (F0b Walking skeleton, F1 Sell, F1b Layered models, F2 Retrieve, F3 Cancel, F4 Renew). The 253 executed requests include each item's own setup calls and read-backs (note 3). The CLI ends with "Postman credentials are required for publishing run details to Postman cloud", which is only about publishing | PASS |
| 9 | — | AC1 · stop the API | Ctrl+C in the terminal from step 2. Then `curl -s -o /dev/null -w "%{http_code}" http://localhost:5080/health/live` | Nothing listens on 5080; curl prints `000` | The API ran with no interactive console here, so I stopped it with `taskkill /PID <pid of Uinsure.Policies.Api.exe> /T /F` (note 2). Then `netstat` showed nothing on 5080, and curl printed `000` | PASS |

### B. Every link in `README.md` resolves (AC1)

There are 14 links on 11 targets. Each anchor was checked against the target's headings, using GitHub's
heading-to-anchor rule (lower-case, punctuation dropped, spaces to hyphens), and outside code fences.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 10 | — | AC1 · link | README 7, 44, 92: `docs/requirements.md` | The file exists | Exists | PASS |
| 11 | — | AC1 · link | README 47: `docs/requirements.md#2-functional-requirements` | A heading with that anchor | `## 2. Functional requirements` (requirements.md 45) | PASS |
| 12 | — | AC1 · link | README 49: `docs/requirements.md#sell-must-1` | A heading with that anchor | `### Sell (MUST-1)` (53) | PASS |
| 13 | — | AC1 · link | README 56: `docs/requirements.md#cancel-should-1` | A heading with that anchor | `### Cancel (SHOULD-1)` (72) | PASS |
| 14 | — | AC1 · link | README 61: `docs/requirements.md#renew-should-2` | A heading with that anchor | `### Renew (SHOULD-2)` (86) | PASS |
| 15 | — | AC1 · link | README 64: `docs/requirements.md#could-haves-all-in-scope` | A heading with that anchor | `### Could-haves (all in scope)` (97) | PASS |
| 16 | — | AC1 · link | README 75: `docs/design.md#security-scope` | A heading with that anchor | `## Security scope` (design.md 183) | PASS |
| 17 | — | AC1 · link | README 81, 90: `infrastructure/README.md` | The file exists | Exists | PASS |
| 18 | — | AC1 · link | README 83: `docs/design.md#known-limits` | A heading with that anchor | `## Known limits` (208) | PASS |
| 19 | — | AC1 · link | README 84: `docs/design.md#behaviours-of-the-renewal-and-input-handling` | A heading with that anchor | `## Behaviours of the renewal and input handling` (234) | PASS |
| 20 | — | AC1 · link | README 88: `docs/design.md` | The file exists | Exists | PASS |

### C. Each README assumption says what its requirements row says (AC1)

One row per bullet in README "Assumptions" (lines 46-71). The requirements citation is to `docs/requirements.md`
at the line.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 21 | — | AC1 · assumption | README 46-47 (today is Europe/London, injected clock) vs requirements 51 | Same meaning | Requirements 51: "today in Europe/London", from the injected `TimeProvider`, passed to the domain as a `DateOnly`. Same | PASS |
| 22 | — | AC1 · assumption | README 48-49 (client chooses the reference; trimmed, upper-cased; duplicate `409`, SELL-1) vs 57 | Same meaning | SELL-1: client supplies it, trimmed and upper-cased, `abc-1` = `ABC-1`, duplicate 409. Same. The README omits the pattern and its 400, which sit in the "Errors" bullet's "400 for shape" | PASS |
| 23 | — | AC1 · assumption | README 50 (SELL-2: today to today + 60 inclusive, no backdating) vs 58 | Same meaning | SELL-2: "between today and today + 60, inclusive. No backdating". Same | PASS |
| 24 | — | AC1 · assumption | README 51-52 (SELL-3: start + 1 year − 1 day; 29 Feb start ends 28 Feb) vs 59 | Same meaning | SELL-3: "start + 1 year − 1 day … A 29 Feb start ends on 28 Feb". Same | PASS |
| 25 | — | AC1 · assumption | README 53 (SELL-5: 16 or older on the start date) vs 61 | Same meaning | SELL-5: "16 or older on the start date (a 16th birthday on the start date passes)". Same | PASS |
| 26 | — | AC1 · assumption | README 54 (amount supplied by the client, above zero, SELL-7, RENEW-5; pricing out of scope) vs 63, 94, 119 | Same meaning | SELL-7 "greater than 0", RENEW-5 "client supplies … greater than 0", 119 "Out of scope: … pricing". Same | PASS |
| 27 | — | AC1 · assumption | README 55-56 (CANCEL-1b cooling-off to start + 14 inclusive; CANCEL-1c pro-rata over 365 or 366 days, cancellation day used) vs 77-78 | Same meaning | CANCEL-1b "start + 14 days, inclusive"; CANCEL-1c "actual days in the term (365 or 366). The cancellation day counts as used". Same | PASS |
| 28 | — | AC1 · assumption | README 57 (CANCEL-6: 2 dp, away from zero, one place) vs 83 | Same meaning | CANCEL-6: "2dp, `MidpointRounding.AwayFromZero`, applied in one place". Same | PASS |
| 29 | — | AC1 · assumption | README 58-59 (date optional, defaults to today, no past dates CANCEL-3; on or before end of cover CANCEL-7; twice is `409` CANCEL-4) vs 80, 84, 81 | Same meaning | CANCEL-3 "Optional, defaults to today … No past dates (422)"; CANCEL-7 "on or before the latest term's end"; CANCEL-4 "409". Same ("end of cover" = the latest term's end) | PASS |
| 30 | — | AC1 · assumption | README 60-61 (RENEW-4 adds a term from the day after the current end; window 30 days before the latest end to the end, inclusive, 31 days; example term ending 16 Oct 2027 → 16 Sep to 16 Oct 2027) vs 90, 93 | Same meaning; the example's arithmetic holds | RENEW-1/2 "end − 30 days to the end date, inclusive, measured from the latest term's end"; RENEW-4 "starting the day after the current end date, under the same reference". 16 Oct 2027 − 30 days = 16 Sep 2027 (30 days in September); 16 Sep to 16 Oct inclusive is 31 days. Same | PASS |
| 31 | — | AC1 · assumption | README 62-63 (auto-renew off → unpaid term; a payment method with it → `422` RENEW-3b; cancelled → `409` RENEW-6) vs 92, 95 | Same meaning | RENEW-3b "created unpaid … sends a `paymentMethod` anyway gets 422"; RENEW-6 "409". Same | PASS |
| 32 | — | AC1 · assumption | README 64-65 (COULD-2: a claim means a £0 refund; `POST /policies/{reference}/claims`) vs 102 | Same meaning | COULD-2: "A claim means a £0 refund … recorded with `POST /policies/{reference}/claims`". Same. The README says "a claim", the row says "the policy has a claim": see O3 for the same scope point in design.md | PASS |
| 33 | — | AC1 · assumption | README 66 (no £0 Refund record; response still lists every term with `refundAmount` 0.00) vs 108 | Same meaning | Requirements 108: "still lists every term, with `refundAmount` 0.00 and `ruleApplied` `COULD-2` or `UNPAID-TERM`". The README drops the `ruleApplied` qualifier, so "every term with 0.00" can be read as all terms at 0.00. This is the carried A4 R4 (O4), recorded as an observation | PASS |
| 34 | — | AC1 · assumption | README 67 (status derived from today, never stored) vs 70 | Same meaning | Status row: "Derived from today, never stored: Pending, Active, Expired, or Cancelled". Same | PASS |
| 35 | — | AC1 · assumption | README 68-70 (400 shape, 404 unknown reference, 409 conflicts, 422 list; a 422 lists every broken rule; unknown JSON fields and query parameters are 400) vs 122-124 | Same meaning, the same twelve 422 rules | Requirements 122 lists the same twelve 422 rules in the same order (SELL-2, SELL-4, SELL-5, SELL-7, RENEW-1/2, RENEW-3b, RENEW-5, CANCEL-3, CANCEL-7, COULD-2a, COULD-2b, COULD-3), 409 for state conflicts, 404 for GET-1; 123 "A 422 lists every broken rule"; 124 unknown JSON field or query parameter → 400. Same | PASS |
| 36 | — | AC1 · assumption | README 71 (no authentication; `Location` on renewals and claims points at the policy) vs 119 | Same meaning | 119 "Out of scope: authentication". Same. The `Location` half has no requirements row: it is the API-contract decision stated at `docs/design.md` 155-159 ("`Location: /policies/{reference}`: the policy, not a sub-resource"), checklist B3. Same meaning there (note 4) | PASS |

### D. `docs/readme-checklist.md`: every item ticked, every link resolves (AC2)

Every row has `☑` in Done. Links in this file are relative to `docs/`; the resolved path is given.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 37 | — | AC2 · A1 branching | checklist 19 → `docs/design.md#branching-and-merging` | ☑; the anchor resolves | ☑; `## Branching and merging` (design.md 124) | PASS |
| 38 | — | AC2 · A2 SQLite gap | 20 → `docs/design.md#what-sqlite-cannot-prove` | ☑; resolves | ☑; `### What SQLite cannot prove` (105) | PASS |
| 39 | — | AC2 · A3 prod approval | 21 → `infrastructure/README.md#prod-approval` | ☑; resolves | ☑; `## Prod approval` (infrastructure/README.md 86) | PASS |
| 40 | — | AC2 · A4 one-time setup | 22 → `infrastructure/README.md#one-time-setup-none-of-it-done` | ☑; resolves | ☑; `## One-time setup (none of it done)` (96) | PASS |
| 41 | — | AC2 · A5 "never connected, never run" | 23 → `infrastructure/README.md` | ☑; the file holds the verbatim statement | ☑; the statement is the opening quote block, lines 3-7, starting "**Never connected, never run.**" | PASS |
| 42 | — | AC2 · A6 schema-check claim | 24 → `infrastructure/README.md#schema-check` | ☑; resolves | ☑; `## Schema check` (72); the claim is at 74-75. See O2 (A4 R2) | PASS |
| 43 | — | AC2 · A7 status | 25 → `infrastructure/README.md#status` | ☑; resolves | ☑; `## Status` (12); the sentence is at 14 | PASS |
| 44 | — | AC2 · A8 shape | 26 → `infrastructure/README.md#shape` | ☑; resolves | ☑; `## Shape` (17) | PASS |
| 45 | — | AC2 · A9 no secrets | 27 → `infrastructure/README.md#no-secrets` | ☑; resolves | ☑; `## No secrets` (33) | PASS |
| 46 | — | AC2 · A10 networking | 28 → `infrastructure/README.md#networking` | ☑; resolves | ☑; `## Networking` (42) | PASS |
| 47 | — | AC2 · A11 runtime | 29 → `infrastructure/README.md#runtime` | ☑; resolves | ☑; `## Runtime` (53); `DOTNETCORE\|10.0` "inferred from the documented pattern and a Nov 2025 Q&A" at 55 | PASS |
| 48 | — | AC2 · A12 if F5 cut | 30 → `infrastructure/README.md` | ☑; F5's items say "cut" only if F5 was cut | ☑; the row says "F5 was not cut, so this item does not apply". F5 is `status: done` in the DAG (build.dag.yaml 315), so no A3-A11 row is marked "cut" | PASS |
| 49 | — | AC2 · B1 security scope | 36 → `docs/design.md#security-scope` | ☑; resolves | ☑; `## Security scope` (183) | PASS |
| 50 | — | AC2 · B2 authentication | 37 → `docs/design.md#authentication` | ☑; resolves | ☑; `### Authentication` (150) | PASS |
| 51 | — | AC2 · B3 `Location` | 38 → `docs/design.md#location-on-renewals-and-claims` | ☑; resolves | ☑; ``### `Location` on renewals and claims`` (155) | PASS |
| 52 | — | AC2 · B4 versioning | 39 → `docs/design.md#no-api-versioning` | ☑; resolves | ☑; `### No API versioning` (161) | PASS |
| 53 | — | AC2 · B5 idempotency | 40 → `docs/design.md#idempotency` | ☑; resolves | ☑; `### Idempotency` (165) | PASS |
| 54 | — | AC2 · B6 no £0 refunds | 41 → `docs/design.md#no-refunds-of-zero` | ☑; resolves | ☑; `### No refunds of zero` (171). See O3 (A4 R3) | PASS |

Totals: 18 item rows (A1-A12, B1-B6), 18 ticked, 18 links, 0 broken. Section C of the checklist lists the trail
and node READMEs as not for the project README, so they carry no tick.

### E. Read-throughs and scope

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 55 | — | AC1 · README is short | `wc -l README.md`; read it | A short page: what it is, how to run it, assumptions, with detail linked out | 92 lines. Sections: intro (3-7), How to run it (9-39), Assumptions (41-71), Known limits (73-84), More detail (86-92). Reference material is linked to `docs/design.md` and `infrastructure/README.md` | PASS |
| 56 | — | AC1 · no internal ids or author notes | `grep -nE '\b(F0a\|F0b\|F1b?\|F[2-7]\|inv-[0-9]+\|t[0-9]{1,2}\|R[0-9]\|I[0-9]\|O[0-9]\|TODO\|FIXME\|XXX\|TBD\|attempt\|finding\|verdict\|orchestrator\|subagent\|tester\|builder\|reviewer\|\.claude\|claims\.json\|NOTE)\b' README.md`, then read it | No match; no note addressed to the author or the agents | No match. Read through: no plan, node, task, finding or verdict ids, no agent names, no local paths or emails. The only `http://` strings are the `localhost:5080` URLs | PASS |
| 57 | — | regression · F6 touches no app code | `git merge-base HEAD main`, then `git diff --stat <that sha> HEAD -- src tests` | The diff prints nothing | Merge base `275fc84`; the diff printed nothing. The branch's diff is 9 files, all docs: `README.md`, `docs/design.md`, `infrastructure/README.md`, `docs/readme-checklist.md`, `build.dag.yaml`, `inv-003.plan.md`, one context file and the F5 trail tail (`activity.jsonl`, `loop.jsonl`) | PASS |

## Exploratory notes / defects

### Run, 2026-10-19 (tester agent, t4, at c841ae400af3e55cdec6301dbc9eb809ffc00f7c)

**57 of 57 step rows PASS, 0 FAIL.** No defect raised. One precondition was not met (note 1).

1. **The SQLite file was not deleted, before or after.** The brief asked for
   `src/Uinsure.Policies.Api/policies.development.db*` to be deleted before step 2 and after step 9. The harness
   guard denies a tester any `src/` change made through Bash (G2), and these are binary files, so I could not
   delete them through Edit or Write either. I did not work around the guard. So step 2 started on an existing
   database: EnsureCreated found the tables and created nothing. Every step still passed, but the README's
   first-run path (schema created at start-up on an empty file) was **not** exercised by this run. Because step 9
   killed the process, the `-wal` file was left at 3,984,072 bytes. The files are git-ignored and untracked, and
   `git status` shows only `docs/trace/build/F6/`. **To do before sign-off:** delete the three files, run steps 2-4
   again, and check that `/health/ready` is `Healthy` on the new file.
2. **Stopping the API.** The README says how to start the API, but not how to stop it. Ctrl+C in its terminal is
   the usual way. I had no interactive console, so I used `taskkill /T /F` on `Uinsure.Policies.Api.exe`. A forced
   kill skips the SQLite checkpoint, which is why the `-wal` file stayed large (note 1). Ctrl+C would close it
   cleanly.
3. **"109 requests" is the item count, not the CLI's request count.** README line 34 says 109 requests, which is the
   collection's item count (and the CLI's 109 test-scripts). The CLI summary prints `requests 253`, because items
   also send their own setup calls and read-backs. An assessor who compares the two numbers may think one is wrong.
4. **Order.** Steps 1-9 ran in README order, with the tests (step 7) and Postman (step 8) against the running API.
   Then steps 10-57. Commands ran in Git Bash. The per-project counts in step 7 came from running each test
   project's executable alone after the `dotnet test` run. Step 36's `Location` half and step 30's 31-day example
   are not requirements rows, so I checked them against `docs/design.md` and by calendar arithmetic.
5. **Not run.** I did not open the Scalar page in a browser (step 6 checks the HTTP answer only). I did not run the
   `https` profile.

Observations for David (not step rows, not raised as findings). O1-O4 are the four P2s the A4 reviewer carried
(`docs/trace/build/F6/verdicts/A4-reviewer.json`, R1-R4). The A4 verdict is PASS, and none of them blocks a step
above.

- **O1 (A4 R1). An Azure DevOps claim with no source in the repo.** `infrastructure/README.md` 107, setup step 5,
  says the environments "aren't auto-created from YAML". Nothing in the repo proves it, and the reviewer recalls
  that a YAML deployment job naming a missing environment can create it. Following step 5 works either way. Only
  the stated reason is at risk. Suggested fix: drop the clause or mark it unverified. This is spec wording, so it
  may need your ruling.
- **O2 (A4 R2). The schema-check sentence is contradicted two lines later.** `infrastructure/README.md` 74-75 says
  task inputs, service connections and environments "are not checked". Lines 77-79 and 83 say the checker asserts
  the AzureCLI@2 inputs, the `azureSubscription` form and the environment name `policies-<env>`. 77-79 adds "It
  does not validate those values against Azure DevOps", so a careful reader gets there, but 74-75 reads as false
  on its own. That sentence is the A6 wording the checklist requires word for word (step 42). Suggested fix: say
  "not checked against Azure DevOps", or rule on the verbatim spec text.
- **O3 (A4 R3). design.md reads COULD-2 as per term.** `docs/design.md` 173: "A term with a claim (COULD-2) …
  gets no Refund record". The code applies COULD-2 to every paid term of a policy that has any claim, including a
  future renewal term: `HasClaims` is policy-level, and requirements 102 says "No refund if the policy has a
  claim". Suggested fix: "every paid term of a policy with a claim (COULD-2)".
- **O4 (A4 R4). "every term with `refundAmount` 0.00" in the README.** `README.md` 66 can be read as every term
  being 0.00. Requirements 108 qualifies it with `ruleApplied` `COULD-2` or `UNPAID-TERM`, and design.md 174 has
  the right scope. Step 33 records it. Suggested fix: "still lists each such term, with `refundAmount` 0.00".
- **O5. One unwrapped line.** `README.md` 61 is 239 characters, where the rest of the file wraps at about 115. It
  renders fine on GitHub; it only shows in a plain-text read.

## Sign-off

| | |
|---|---|
| Tester | |
| Date | |
| Commit signed off | |
| `dotnet test` result | __ passed / __ failed / __ skipped |
| Postman CLI result | __ requests / __ assertions / __ failed |
| SQLite file deleted and steps 2-4 re-run (note 1) | ☐ |
| Steps passed | __ of 57 |
| Defects raised (finding ids) | |
| Verdict | ☐ Reject (findings above)  ☐ Accept F6 |
| Signature | |
