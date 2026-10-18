# QA script — build / F1b — Layered models and interfaces

Rule IDs: none of its own. F1b re-proves F1's rules unchanged: SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7
Branch: feature/build/F1b   Worktree: .claude/worktrees/build-F1b
Commit under test: 8b13192 (the last code commit; a0c4263 after it changes only the plan, and this script's own commit adds only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080 and https://localhost:7080   Environment: local (`tests/postman/local.postman_environment.json`)

F1b changes no endpoint and adds no rule. It moves storage onto internal Persistence records with a Mapperly
mapper, puts the layers behind interfaces (`IPolicyService`, `IReferenceGenerator`), moves `Version` onto
`PolicyRecord`, adds `Policy.LatestTerm`, `CurrentTerm(today)` and `StatusOn(today)` to the Domain, logs a
client-aborted request at Information, and makes an amount with more than 2 decimal places, or above
1,000,000.00, a 400 shape error (ruling H6). So this script has four jobs: show the F1 Sell folder is unchanged
and still green (section A), run the new money rows (B and C), check the layering (D), and point at the tests for
everything a hand can't reach (E).

The Rule column holds a bare rule id (`SELL-n`) wherever a step checks a rule. Other values: `SHAPE` for a 400
shape row that guards no numbered rule; `A12` to `A15` for the layering checks, named after the architecture tests
that enforce them; `—` for plumbing (concurrency, client abort, the per-request record map). Finding ids from
`docs/trace/build/F1b/findings/A1.md` are cited in the steps as `A1/I1` to `A1/I5`: I1 and I2, an extreme negative
amount overflowed the scale check into a 500 instead of the 422 SELL-7; I3, no negative row in the shape tests;
I4, `ReferenceGenerator` kept a public static way around its interface; I5, no two-term Domain test.

Commands are for PowerShell, run from the worktree root. Inside the tables, `\|` is a Markdown escape: type a
plain `|` (the PowerShell pipe).

## Preconditions

- [ ] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: __ / __ / __). Expected: 339 / 0 / 0.
- [ ] Ports 5080 and 7080 are free (`netstat -ano | findstr ":5080 :7080"` prints no LISTENING line). A running API also locks the build output, so stop it before any `dotnet build` or `dotnet test`.
- [ ] The dev certificate is trusted: `dotnet dev-certs https --check --trust` prints `A trusted certificate was found`.
- [ ] Start from an empty database, so the fixed `QA-F1B-*` references in the hand steps are free. With the API stopped: `Remove-Item src/Uinsure.Policies.Api/policies.development.db*`. Start-up recreates it (`EnsureCreated`). The table schema is the same as F1's (the migration was regenerated, and its `InitialCreate` body is unchanged), but an empty file removes the question.
- [ ] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).

### Starting the API

`dotnet run --project src/Uinsure.Policies.Api`. With no `--launch-profile`, `dotnet run` takes the first profile,
`https`. The console shows `Now listening on: https://localhost:7080`, `Now listening on: http://localhost:5080`
and `Hosting environment: Development`. Keep the console in view: step 25 reads it.

### Shell setup for the hand steps (H1–H5)

Paste this once into the PowerShell window you use for the hand steps. UK today comes from Europe/London, as in the API.

```powershell
$ukToday = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'GMT Standard Time').Date
$start   = $ukToday.AddDays(22).ToString('yyyy-MM-dd')
function Sell([string]$reference, [string]$amount) {
  ('{"reference":"' + $reference + '","type":"Household","startDate":"' + $start + '","amount":' + $amount + ',"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}') |
    curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-"
}
```

The amount is passed as text and written into the JSON as it stands, so `527.180` reaches the API with its
trailing zero and `-79228162514264337593543950335` is not turned into a PowerShell number first. The JSON is
piped to curl so that PowerShell does not strip its quotes.

### Hand requests

```powershell
# H1 [SHAPE] 527.180: three digits after the point, but a whole number of pence
Sell 'QA-F1B-TRAIL' '527.180'
curl.exe -s -i http://localhost:5080/policies/QA-F1B-TRAIL

# H2 [SELL-7] minus 10 to the 27 (finding A1/I1, A1/I2)
Sell 'QA-F1B-E27' '-1000000000000000000000000000'
curl.exe -s -i http://localhost:5080/policies/QA-F1B-E27

# H3 [SELL-7] decimal.MinValue, the most negative amount that binds (finding A1/I3)
Sell 'QA-F1B-MIN' '-79228162514264337593543950335'
curl.exe -s -i http://localhost:5080/policies/QA-F1B-MIN

# H4 [SELL-7] -1,000,000.01: the 1,000,000.00 cap bounds positive amounts only
Sell 'QA-F1B-NEG1M' '-1000000.01'
curl.exe -s -i http://localhost:5080/policies/QA-F1B-NEG1M

# H5 [SHAPE] one below decimal.MinValue: it does not bind as a decimal at all
Sell 'QA-F1B-BEYOND' '-79228162514264337593543950336'
```

## Steps

### A. The F1 Sell folder, unchanged and green (API running in Development, from "Starting the API")

F1b adds no request to `F1 Sell` and changes none. The expected values for each request are the ones signed off
in `docs/qa/build-F1.md` steps 1–38; this section re-runs them on the F1b code. Every 201 row reads the policy back
by `GET` and checks every value sold, which now crosses Api DTO → Domain → Persistence record → Domain → Api DTO.
Every rejected row checks `GET` → 404, so nothing was stored.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | SELL-1 | SELL-1 · new reference → 201; SELL-1 · lower-case reference with spaces is normalised → 201; SELL-1 · reference of 20 characters → 201; SELL-1 · duplicate reference → 409; SELL-1 · duplicate differing only in case → 409; SELL-1 · reference of 2 characters → 400; SELL-1 · reference of 21 characters → 400; SELL-1 · reference with an underscore → 400 | Send each (folder `F1 Sell`) | The 201s: `Location: /policies/{reference}` (upper-cased) and a full GET read-back. The 409s: `type` /problems/sell-1, `title` "Policy reference already exists", `ruleId` SELL-1, `reference`, no `violations`, and the original (BuyToLet, 412.34) reads back unchanged. The 400s: `errors.reference`, no `ruleId` | | |
| 2 | SELL-2 | SELL-2 · start today → 201; SELL-2 · start today+60 → 201; SELL-2 · start today+61 → 422; SELL-2 · start yesterday → 422 | Send each | The 201s read back `startDate` (and `endDate` = today + 1 year − 1 day for the first). The 422s: `type` /problems/sell-2, `title` "Start date outside the sale window", `ruleId` SELL-2, `violations` = [SELL-2], `earliest` = today, `latest` = today + 60 | | |
| 3 | SELL-2 | SELL-2 · start today+61, 4 policyholders, one under 16, amount 0 → 422 | Send | `422`, `ruleId` SELL-2 (the first in rule order), `detail` "4 rules failed.", `violations[].ruleId` = [SELL-2, SELL-4, SELL-5, SELL-7] in that order | | |
| 4 | SELL-3 | SELL-3 · end = start + 1 year − 1 day → 201; SELL-3 · endDate sent → 400 | Send each | `201`, GET `endDate` and `terms[0].endDate` = start + 1 year − 1 day, one term with `termNumber` 1. Then `400`, `errors["$.endDate"]` | | |
| 5 | SELL-4 | SELL-4 · 3 policyholders → 201; SELL-4 · 0 policyholders → 422; SELL-4 · 4 policyholders → 422 | Send each | `201`, GET has 3 policyholders in the order sent. Then `422` /problems/sell-4, `title` "Wrong number of policyholders", `violations` = [SELL-4], `count` 0; and the same with `count` 4 | | |
| 6 | SELL-5 | SELL-5 · 16th birthday on the start date → 201; SELL-5 · 16th birthday the day after the start date → 422 | Send each | `201` and the GET round trip. Then `422` /problems/sell-5, `title` "Policyholder under 16", `violations` = [SELL-5], `index` 1 | | |
| 7 | SELL-6 | SELL-6 · payment for the full amount → 201; SELL-6 · paymentMethod missing → 400 | Send each | `201`, GET: one payment, `type` Card, `amount` 389.5, a `PAY-…` `reference`, and `terms[0].paymentReference` equal to it (the reference now comes from the injected `IReferenceGenerator`). Then `400`, `errors.paymentMethod`, no `ruleId` | | |
| 8 | SELL-7 | SELL-7 · amount 0.01 → 201; SELL-7 · amount 0 → 422; SELL-7 · amount negative → 422 | Send each | `201`, GET `amount` 0.01. Then `422` /problems/sell-7, `title` "Policy amount must be greater than zero", `violations` = [SELL-7], `amount` 0; and the same with `amount` -1. The extreme negatives are steps 21–23 and 32 | | |
| 9 | SHAPE | SHAPE · postcode of 8 characters → 201, and the eleven `SHAPE · … → 400` requests in `F1 Sell` | Send each | As `docs/qa/build-F1.md` steps 11 and 27–36: the 201 reads back as sold; each 400 has the validation `title`, its own `errors` key, a `traceId` and no `ruleId` | | |
| 10 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | whole folder `F1 Sell` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F1 Sell"` | Every line is `Pass`. Requests 75 / 0 failed, assertions 73 / 0 failed: the same totals F1 signed off. The closing "Postman credentials are required…" line is about cloud publishing only | | |
| 11 | SELL-1 | whole folder over HTTPS | The same command with `--env-var "baseUrl=https://localhost:7080"` added | The same totals, requests 75 / 0 and assertions 73 / 0, every URL `https://localhost:7080/…` | | |
| 12 | SELL-1 | — (the collection file) | `git diff --numstat main...HEAD -- tests/postman`, then `git diff main...HEAD -- tests/postman \| Select-String '^-[^-]'` | The first prints `341 0 tests/postman/Uinsure.Policies.postman_collection.json`: lines added, none removed. The second prints nothing. The added lines are the `F1b Layered models` folder (section B), so no F1 request or assertion was changed | | |

### B. The F1b folder: the money 400 rows (same API)

Each request makes its own `F1B-…` reference and computes its start date (UK today + 22) from the real today.
Every 400 has `application/problem+json`, `title` "One or more validation errors occurred.", `errors.amount`, and
no `ruleId`, and its GET is `404`, so nothing ambiguous was stored.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 13 | SHAPE | SHAPE · amount with 2 decimal places → 201 | Send (`803.47`, DirectDebit) | `201`, `Location: /policies/{reference}`. GET → `200`, the full read-back as sold, `amount` 803.47, one DirectDebit payment of 803.47 linked to term 1 | | |
| 14 | SHAPE | SHAPE · amount 1,000,000.00 → 201 | Send (`1000000.00`) | `201`. GET → `200`, full read-back, `amount` 1000000: the cap itself is accepted | | |
| 15 | SHAPE | SHAPE · amount with 3 decimal places → 400 | Send (`527.185`) | `400`, `errors.amount`, no `ruleId`; GET → `404`. The amount is rejected, never rounded to 527.18 or 527.19 | | |
| 16 | SHAPE | SHAPE · amount 0.001 → 400 | Send | `400`, `errors.amount`, no `ruleId`; GET → `404`. Below a penny is a scale error, so it is the 400, not the 422 SELL-7 of a zero amount | | |
| 17 | SHAPE | SHAPE · amount 1,000,000.01 → 400 | Send | `400`, `errors.amount`, no `ruleId`; GET → `404` | | |
| 18 | SHAPE | whole folder `F1b Layered models` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F1b Layered models"` | Every line is `Pass`. Requests 10 / 0 failed, assertions 10 / 0 failed (5 requests plus their 5 GETs) | | |
| 19 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | the whole collection | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json` | Requests 87 / 0 failed, assertions 85 / 0 failed (`F0b` 2 + `F1 Sell` 75 + `F1b` 10 requests) | | |

### C. Hand requests: the money edges Postman doesn't carry (same API; the shell setup and H1–H5 above)

The Postman folder has only the ordinary negative row (`-1`, step 8). These steps send the A1 extremes by hand;
step 32 runs the tests that pin them.

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 20 | SHAPE | H1 | Run H1 (both lines) | The POST is `201` with `Location: /policies/QA-F1B-TRAIL`. `527.180` is a whole number of pence, so the scale check accepts it: the trailing zero is not a third decimal place. The GET is `200` with `amount` 527.18, `terms[0].amount` 527.18 and `payments[0].amount` 527.18 (see the exploratory note on the POST body) | | |
| 21 | SELL-7 | H2 | Run H2 (both lines) | The POST is `422`, `type` /problems/sell-7, `title` "Policy amount must be greater than zero", `ruleId` SELL-7, `amount` -1000000000000000000000000000, `violations` = [SELL-7], `instance` /policies and a `traceId`. Never a 500 (finding A1/I1 and I2). The GET is `404` | | |
| 22 | SELL-7 | H3 | Run H3 (both lines) | The POST is `422` /problems/sell-7, `ruleId` SELL-7, `amount` -79228162514264337593543950335, `violations` = [SELL-7]. Never a 500. The GET is `404` | | |
| 23 | SELL-7 | H4 | Run H4 (both lines) | The POST is `422` /problems/sell-7, `ruleId` SELL-7, `amount` -1000000.01, `violations` = [SELL-7]: not the `400` of step 17, because the cap is on positive amounts only. The GET is `404` | | |
| 24 | SHAPE | H5 | Run H5 | `400`, `errors["$.amount"]` (the value cannot be converted to a decimal), the validation `title`, a `traceId` and no `ruleId`. It never reaches the scale check or SELL-7 | | |
| 25 | SELL-7 | — (the API console) | Look at the API console output for H2–H5 | No `fail:` or `crit:` line: the 500 path (`UnexpectedExceptionHandler`) logs at Error, and nothing here went down it. One `warn: Microsoft.EntityFrameworkCore.Query[20504]` line (more than one collection `Include`, single query) appears at the first lookup after start-up; it is not from these requests | | |

### D. Layering, by inspection (API may stay running; these read source only)

Each step names the architecture tests that enforce it; step 33 runs them.

| # | Rule | Check | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 26 | A12 | Persistence records: internal, and the only EF-mapped types (`PersistenceRecordTests`) | `Select-String -Path src/Uinsure.Policies.Persistence/Records/*.cs -Pattern 'class \w+Record'` | Five lines, each `internal sealed class …Record`: `PaymentRecord`, `PolicyholderRecord`, `PolicyRecord`, `PropertyRecord`, `TermRecord`. No public record | | |
| 27 | A12 | The concurrency token is `PolicyRecord.Version` | `Select-String -Path src/Uinsure.Policies.Persistence/Configurations/PolicyConfiguration.cs -Pattern 'Version'` and `Select-String -Path src/Uinsure.Policies.Persistence/Records/PolicyRecord.cs -Pattern 'Version'` | `PolicyConfiguration.cs:19: builder.Property(p => p.Version).IsConcurrencyToken();` and `PolicyRecord.cs:14: public Guid Version { get; set; }`. The repository sets a new `Version` on each save of a changed record | | |
| 28 | A13 | `Policy.Restore` is called only from Persistence (`PolicyRestoreTests`) | `Get-ChildItem src -Recurse -Filter *.cs \| Where-Object FullName -notmatch '\\obj\\' \| Select-String 'Policy\.Restore\('` | One match: `src/Uinsure.Policies.Persistence/PolicyRecordMapper.cs`. The mapper is a Mapperly `[Mapper]`; `Restore` is its hand-written member | | |
| 29 | A14 | No `Version` and no EF-only constructor in the Domain (`DomainPersistenceIgnoranceTests`) | `Get-ChildItem src/Uinsure.Policies.Domain -Recurse -Filter *.cs \| Where-Object FullName -notmatch '\\obj\\' \| Select-String '\bVersion\b'` | No match | | |
| 30 | A15 | The layers meet at interfaces (`LayerSeamTests`) | Open `src/Uinsure.Policies.Api/Controllers/PoliciesController.cs` line 12 and `src/Uinsure.Policies.Api/Program.cs` lines 57–58; then `Select-String -Path src/Uinsure.Policies.Services/ReferenceGenerator.cs -Pattern 'static'` | The controller is `PoliciesController(IPolicyService service)`. Program registers `IReferenceGenerator` → `ReferenceGenerator` and `IPolicyService` → `PolicyService`. The `Select-String` prints nothing: `ReferenceGenerator` has one instance member, `Payment()`, and no `static`, so nothing can reach a payment reference around the interface (finding A1/I4) | | |
| 31 | — | The repository's loaded-record map is per request, never static | Open `src/Uinsure.Policies.Persistence/PolicyRepository.cs` (the `_records` field) and `src/Uinsure.Policies.Persistence/PersistenceServiceCollectionExtensions.cs` line 29 | `_records` is a `private readonly` instance field (the only `static` in the file is the `IsUniqueViolation` helper). The repository is registered with `AddScoped`, so each request gets its own map | | |

### E. Rows that need a fake clock, an injected fault or a log probe: covered by tests

Stop the API first (it locks the build output).

| # | Rule | Covered by test | Command | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 32 | SELL-7 | `Amount_of_minus_10_to_the_27_is_rejected_with_422_SELL_7`, `Amount_of_decimal_MinValue_is_rejected_with_422_SELL_7`, `Amount_of_minus_1_000_000_01_is_rejected_with_422_SELL_7` (`SellAmountShapeTests`), and `Amount_of_minus_10_to_the_27_is_not_a_500_and_logs_no_error`, `Amount_of_decimal_MinValue_is_not_a_500_and_logs_no_error` (`SellAmountOverflowTests`, run in Production, where a 500 is logged at Error) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Amount_of_minus_10_to_the_27_is_rejected_with_422_SELL_7" --filter-method "*.Amount_of_decimal_MinValue_is_rejected_with_422_SELL_7" --filter-method "*.Amount_of_minus_1_000_000_01_is_rejected_with_422_SELL_7" --filter-method "*.Amount_of_minus_10_to_the_27_is_not_a_500_and_logs_no_error" --filter-method "*.Amount_of_decimal_MinValue_is_not_a_500_and_logs_no_error"` | 5 passed. Findings A1/I1, I2 and I3: each extreme negative is the 422 SELL-7 with its `amount`, is never a 500, writes no Error-level log, and is not stored | | |
| 33 | A12 | `PersistenceRecordTests` (A12, 4), `PolicyRestoreTests` (A13, 3), `DomainPersistenceIgnoranceTests` (A14, 2), `LayerSeamTests` (A15, 6, including `ReferenceGenerator_declares_no_public_static_method` from A1/I4) | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.PersistenceRecordTests" --filter-class "Uinsure.Policies.Architecture.Tests.PolicyRestoreTests" --filter-class "Uinsure.Policies.Architecture.Tests.DomainPersistenceIgnoranceTests" --filter-class "Uinsure.Policies.Architecture.Tests.LayerSeamTests"` | 15 passed | | |
| 34 | A12 | the whole Architecture.Tests project (A1–A15 and `RuleCoverageTests`) | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests` | 66 passed | | |
| 35 | SHAPE | `SellAmountShapeTests` (the 400 rows at 527.185, 0.001 and 1,000,000.01; the 201 rows at 803.47 and 1,000,000.00; the three negative rows of step 32) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.SellAmountShapeTests"` | 8 passed | | |
| 36 | — | `ConcurrencyConflictTests`: a stale save of a sold policy answers 409 `/problems/concurrency` with no `ruleId` and the detail "Retry the request.", the policy still reads back as sold, and the token is `Uinsure.Policies.Persistence.Records.PolicyRecord`'s `Version` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.ConcurrencyConflictTests"` | 3 passed. F1 has no endpoint that changes a stored policy, so the factory injects the stale save | | |
| 37 | SELL-1 | `SellDuplicateRaceTests` (Api.Tests, 2) and `PolicyServiceSellRaceTests` (Services.Tests, 2): a sale that races past the duplicate check and hits the unique index answers SELL-1 alone; a concurrency conflict is not turned into SELL-1 | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.SellDuplicateRaceTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceSellRaceTests"` | 2 passed, then 2 passed | | |
| 38 | — | `ClientAbortedRequestTests`: a sale the client aborts gets no 500 problem body, is never logged at Error, is logged at Information by the app, and is not stored | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.ClientAbortedRequestTests"` | 4 passed. Aborting mid-request can't be timed by hand; the factory holds the lookup until the client cancels | | |
| 39 | — | `PolicyTermsTests` and `PolicyStatusTests` (Domain.Tests): `LatestTerm`, `CurrentTerm(today)` and `StatusOn(today)` on each side of each term boundary, on one-term and two-term policies (A1/I5). Untraited: F2 adds the GET-1 traits | `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.PolicyTermsTests" --filter-class "Uinsure.Policies.Domain.Tests.PolicyStatusTests"` | 22 passed | | |
| 40 | SELL-6 | `PolicyServiceGetDetailsTests`, `PolicyServiceSellDetailsTests` and `ReferenceGeneratorTests` (Services.Tests): the service returns the domain's current term and status on UK today, including at 23:30 UTC in BST, and payment references come through `IReferenceGenerator` | `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceGetDetailsTests" --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceSellDetailsTests" --filter-class "Uinsure.Policies.Services.Tests.ReferenceGeneratorTests"` | 8 passed | | |
| 41 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | every test traited with the rule, across Domain, Services and Api tests | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-1"`, then the same for SELL-2 … SELL-7 | Passed, with no failures: SELL-1 28, SELL-2 26, SELL-3 13, SELL-4 11, SELL-5 16, SELL-6 18, SELL-7 13. Against F1's sign-off (27, 26, 13, 11, 16, 18, 10) no count fell: SELL-1 gained the Services race test and SELL-7 the three negative rows | | |
| 42 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | `RuleCoverageTests` | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`, then open `tests/rule-coverage.pending.txt` | 11 passed. The pending file still has 23 IDs and no SELL ID: F1b adds no rule and takes none out | | |
| 43 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | the whole suite | `dotnet test --solution Uinsure.Policies.slnx` | 339 passed, 0 failed, 0 skipped (F1 signed off at 277) | | |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- For your ruling, not raised as a finding: the **201 body echoes the amount as sent**, but the GET reads it back
  at 2 decimal places. H1 (`527.180`) gave a POST body with `"amount":527.180` (and the same in `terms[0]` and
  `payments[0]`), and the GET gave `527.18`. The two are the same JSON number, and the stored value is 527.18,
  so nothing ambiguous is stored. A client that compares the text of the two bodies would see a difference. No
  test covers 527.180; the scale check (`amount % 0.01m != 0`) accepts it by design.
- For your ruling, not raised as a finding: H5 (one below `decimal.MinValue`) gets the framework's own binding
  message, ``"$.amount":["The JSON value could not be converted to System.Nullable`1[System.Decimal]. Path: $.amount | LineNumber: 0 | BytePositionInLine: 113."]``,
  plus `"request":["The request field is required."]`. That names a .NET type and a byte position. It is the
  same path any unbindable JSON value takes (F1's malformed and wrong-type rows), so it is not new in F1b. The
  security verdict at 8b13192 is PASS.
- Step 25's `warn:` line (EF Core `MultipleCollectionIncludeWarning`) comes from `GetByReference` loading terms and
  policyholders in one query. It is a performance hint, not a fault.
- How these expectations were checked: on 2026-10-18 the tester ran section A to E against 8b13192 (HEAD a0c4263)
  with Git Bash `curl` and the Postman CLI 1.75.0: `F1 Sell` 75 / 0 and 73 / 0 on both ports, `F1b Layered models`
  10 / 0 and 10 / 0, the whole collection 87 / 0 and 85 / 0; H1 to H5 gave the statuses and bodies above, with
  no `fail:` line in the console; every command in section E, and section D's facts, read with `grep`. Deviations:
  the database was not cleared first (the gate run's file was reused), and the hand requests used `QAV-F1B-*`
  references, not this script's `QA-F1B-*`, so your run's fixed references stay free either way. The harness
  doesn't let the tester run `powershell.exe`, so the shell setup, the `Sell` function and the `Select-String` /
  `Get-ChildItem` lines have not been run as written. If one misbehaves, note it here: that is a script defect,
  not an API defect.

## Sign-off

| | |
|---|---|
| Tester | |
| Date | |
| Commit signed off | |
| `dotnet test` result | __ / __ / __ (total / failed / skipped) |
| Postman CLI result | `F1 Sell`: requests __ / failed __; assertions __ / failed __. `F1b Layered models`: requests __ / failed __; assertions __ / failed __ |
| Steps passed | __ of 43 |
| Defects raised (finding ids) | |
| Verdict | ☐ Accept F1b  ☐ Reject (findings above) |
| Signature | |
