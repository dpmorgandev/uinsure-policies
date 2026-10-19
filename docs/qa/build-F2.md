# QA script — build / F2 — Retrieve

Rule IDs: GET-1 (and SELL-7 for the two extreme-negative echo rows F2 pins)
Branch: feature/build/F2   Worktree: .claude/worktrees/build-F2
Commit under test: 143e5ba (the last code or test commit; both A3.2 verdicts are PASS at it. 4772bc9 after it changes only the plan, and this script's own commit adds only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080   Environment: local (`tests/postman/local.postman_environment.json`)

F2 adds `GET /policies/{reference}`. It returns the full `PolicyResponse`: `status` (Pending, Active or Expired,
derived from UK today on each request), `currentTermNumber` and the current term's `startDate`, `endDate` and
`amount`, `hasClaims` as stored, the `terms[]` history, `payments`, and `refunds`, `claims` and `cancellation`
(empty or null until F3). A reference with no policy is a 404 `/problems/get-1`. F2 also writes every money value
with exactly 2 decimal places and never rounds one, which closes the read-back-scale point carried over from the
F1b sign-off (`1000000.0`, `803.4`, `100.0`). It rejects an unknown query key even when it shares a name with a
route, body or special parameter.

The Rule column holds a bare rule id: `GET-1` for retrieve rows, `SELL-7` for the extreme-negative echo rows, and
`SHAPE` for a 400 shape row that guards no numbered rule. Finding ids from `docs/trace/build/F2/findings/A1.md` and
`A2.md` are cited as `A1/I1` to `A2/I6`; `A3/R1` is the reviewer finding in `docs/trace/build/F2/verdicts/A3-reviewer.json`.

Commands are for PowerShell, run from the worktree root. Inside the tables, `\|` is a Markdown escape: type a
plain `|` (the PowerShell pipe).

## Preconditions

- [x] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: 374 / 0 / 0). Expected: 374 / 0 / 0.
- [x] Ports 5080 and 7080 are free (`netstat -ano | findstr ":5080 :7080"` prints no LISTENING line). A running API also locks the build output, so stop it before any `dotnet build` or `dotnet test`.
- [x] The fixed `QA-F2-*` references of the hand steps are free. Either start from an empty database (with the API stopped: `Remove-Item src/Uinsure.Policies.Api/policies.development.db*`; start-up recreates it with `EnsureCreated`), or, once the API is up, check that a GET of each of `QA-F2-NEAR`, `QA-F2-NEARX`, `QA-F2-QREQ`, `QA-F2-E27` and `QA-F2-MIN` gives `404`.
- [x] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).

Execution record (2026-10-18 23:52 to 2026-10-19 00:02 UTC, i.e. 2026-10-19 00:52 to 01:02 UK time, BST; UK today
2026-10-19 throughout; worktree HEAD beba48e3f7f4efc948d3542dffce87bfe2de33df, whose `src/` and `tests/` are identical to
143e5ba). Precondition evidence: the full suite printed `total: 374, failed: 0, succeeded: 374, skipped: 0`; netstat
showed no LISTENING line on 5080 or 7080 before start-up and none after the stop; the orchestrator had deleted
`policies.development.db`, and at start-up the console showed `EnsureCreated` issuing `CREATE TABLE "Policies"` and the
other tables (a fresh database); a GET of each of the five `QA-F2-*` references gave `404` before any hand step;
Postman CLI 1.75.0 ran the collection and the `local` environment from `tests/postman/` at this HEAD.

### Deviations from the script as written

Expected values and step text are unchanged. The harness refuses `powershell.exe`, so each step's equivalent was run
in Git Bash:

1. **Stale WAL files.** The `.db` was already deleted, but `policies.development.db-shm` and `policies.development.db-wal`
   (457 KB) were still there. The guard refuses any tester deletion under `src/`, so `Remove-Item …db*` could not be
   completed. I relied on the precondition's second option, a `404` for each of the five `QA-F2-*` references once the
   API was up. The console's `CREATE TABLE` lines show the database was built fresh: SQLite discards a WAL whose
   database has zero pages.
2. **Ports.** `netstat -ano | grep -E ":5080 |:7080 " | grep LISTENING` replaced `findstr`.
3. **Start and stop.** `dotnet run --project src/Uinsure.Policies.Api` ran as a background task, with no `--launch-profile`.
   The first profile (`https`) applied: `Now listening on: https://localhost:7080`, `Now listening on: http://localhost:5080`,
   `Hosting environment: Development`. No step in this script sends to 7080. The API was stopped with
   `taskkill //IM Uinsure.Policies.Api.exe //F`.
4. **Steps 1–14.** Each request was run on its own as
   `postman collection run … -e … -i "<request name>" --verbose`, and its request and response bodies were read from
   the verbose output. Selecting by name works; the `folder/sub-folder/request` path form gave "Unable to find request
   or folder". Verbose output doesn't print response headers. The `Location` in step 5–8 is the request's own
   `pm.test`, which asserts `/policies/{reference}`, plus the read-back GET to that path.
5. **Step 15** ran twice: once to read the lines and the totals, once to count the `Pass` lines (26). Steps 1–15 and
   the second run of 15 each made their own `F2-…` data, so later runs met more stored policies than a single pass would.
   Every request makes a unique reference, so no result depends on that.
6. **Step 18.** `grep -E '^(-[^-]|@@)'` replaced `Select-String '^(-[^-]|@@)'`, with the same pattern.
7. **Shell setup and H1–H6.** `$ukToday` was taken as `date -u -d "+1 hour" +%F`, because Git Bash has no Europe/London
   zone data and UK time is BST (UTC+1) until 2026-10-25. That gave 2026-10-19, so `$start` = 2026-11-10. The `Sell`
   function was not defined. Each call was sent as its literal expansion,
   `echo '<the Sell JSON with reference, start 2026-11-10 and amount as text>' | curl -s -i -X POST "http://localhost:5080/policies<query>" -H "Content-Type: application/json" --data-binary "@-"`,
   with Git Bash `curl` in place of `curl.exe`. The amounts were written into the JSON as text, as the script intends.
8. **Step 26.** The console was read from the background task's output file:
   `grep -c '^fail:'`, `'^crit:'` and `'^warn:'`, plus the warning's text.
9. **Section D** ran after the API had stopped, as the section says. Step 38's per-project split came from three more
   runs of the same trait filter, one per project (`--project tests/Uinsure.Policies.{Api,Domain,Services}.Tests`).
   Step 40's ID count is `grep -cE '^[A-Z]'` plus a `cat` of the file.

### Starting the API

`dotnet run --project src/Uinsure.Policies.Api`. The console shows `Now listening on: https://localhost:7080`,
`Now listening on: http://localhost:5080` and `Hosting environment: Development`. Keep the console in view: step 26
reads it.

### Shell setup for the hand steps (H1–H6)

Paste this once into the PowerShell window you use for the hand steps. UK today comes from Europe/London, as in the
API. The optional third argument of `Sell` is a query string for the POST URL.

```powershell
$ukToday = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'GMT Standard Time').Date
$start   = $ukToday.AddDays(22).ToString('yyyy-MM-dd')
function Sell([string]$reference, [string]$amount, [string]$query = '') {
  ('{"reference":"' + $reference + '","type":"Household","startDate":"' + $start + '","amount":' + $amount + ',"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}') |
    curl.exe -s -i -X POST ("http://localhost:5080/policies" + $query) -H "Content-Type: application/json" --data-binary "@-"
}
```

The amount is passed as text and written into the JSON as it stands, so `-79228162514264337593543950335` does not
become a PowerShell number first. The JSON is piped to curl so that PowerShell does not strip its quotes.

### Hand requests

```powershell
# H1 [GET-1] a reference one character longer than a sold one
Sell 'QA-F2-NEAR' '527.18'
curl.exe -s -i http://localhost:5080/policies/QA-F2-NEAR
curl.exe -s -i http://localhost:5080/policies/QA-F2-NEARX

# H2 [SHAPE] ?reference on a GET: the reference is bound from the route, not the query (finding A2/I4)
curl.exe -s -i "http://localhost:5080/policies/QA-F2-NEAR?reference=X"

# H3 [SHAPE] ?cancellationToken on a GET: a special parameter, never bound from the query (A2/I4, the security P2 note)
curl.exe -s -i "http://localhost:5080/policies/QA-F2-NEAR?cancellationToken=1"

# H4 [SHAPE] ?request on a sale with a valid body: the request is bound from the body (A2/I4)
Sell 'QA-F2-QREQ' '527.18' '?request=x'
curl.exe -s -i http://localhost:5080/policies/QA-F2-QREQ

# H5 [SELL-7] minus 10 to the 27: the echo's exact text (A2/I3)
Sell 'QA-F2-E27' '-1000000000000000000000000000'
curl.exe -s -i http://localhost:5080/policies/QA-F2-E27

# H6 [SELL-7] decimal.MinValue: the echo's exact text (A3/R1)
Sell 'QA-F2-MIN' '-79228162514264337593543950335'
curl.exe -s -i http://localhost:5080/policies/QA-F2-MIN
```

## Steps

### A. The F2 Retrieve folder (API running in Development, from "Starting the API")

Each request makes its own `F2-…` reference and computes its dates from the real UK today. Each GET in `happy path`
first sells its own policy in a pre-request script and then checks that the sale answered `201`. "The full
read-back" means the GET has exactly the contract's members (`reference`, `type`, `status`, `currentTermNumber`,
`startDate`, `endDate`, `amount`, `hasClaims`, `autoRenew`, `policyholders`, `property`, `terms`, `payments`,
`refunds`, `claims`, `cancellation`), every value sold, `currentTermNumber` 1, one term and one payment linked to
it, `hasClaims` false, `refunds` and `claims` `[]`, and `cancellation` null.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | GET-1 | GET-1 · sold policy reads back in full → 200 | Send (folder `F2 Retrieve` / `happy path`). The sale is BuyToLet, 412.34 by Card, start today + 30, auto-renew off, two policyholders (Grace Hopper, Alan Turing), 7 Canal St, Flat 3, Ancoats, M4 6BX | `200`, the full read-back, `status` Pending, `endDate` = start + 1 year − 1 day, `terms[0]` the same dates and 412.34 | POST `201`, GET `/policies/F2-MVEHF2QGG5` `200`: exactly the 16 members; BuyToLet, `status` Pending, `currentTermNumber` 1, start 2026-11-18 (today + 30), `endDate` 2027-11-17, 412.34, `autoRenew` false, Grace Hopper and Alan Turing, 7 Canal St / Flat 3 / Ancoats / M4 6BX; `terms[0]` 2026-11-18 to 2027-11-17, 412.34, linked to the one Card payment; `hasClaims` false, `refunds`/`claims` `[]`, `cancellation` null. 3/3 `pm.test` Pass | PASS |
| 2 | GET-1 | GET-1 · start today reads back Active → 200 | Send (the default sale, starting UK today) | `200`, the full read-back, `status` Active, `currentTermNumber` 1, `startDate` today | POST `201`, GET `200`: `status` Active, `currentTermNumber` 1, `startDate` 2026-10-19 (UK today), `endDate` 2027-10-18; the full read-back of the default sale. 2/2 `pm.test` Pass | PASS |
| 3 | GET-1 | GET-1 · start today+22 reads back Pending → 200 | Send (the default sale: Household, 527.18 by DirectDebit, Katherine Johnson, 48 Quay St, Unit 5, M3 4LZ) | `200`, the full read-back, `status` Pending, `currentTermNumber` 1 | POST `201`, GET `200`: Household, 527.18, DirectDebit, Katherine Johnson, 48 Quay St / Unit 5 / M3 4LZ, start 2026-11-10, end 2027-11-09, `status` Pending, `currentTermNumber` 1, empty history. 2/2 `pm.test` Pass | PASS |
| 4 | GET-1 | GET-1 · lower-case reference finds the policy → 200 | Send (the GET path is the reference in lower case) | `200` for the sold policy: the route reference is trimmed and upper-cased before the lookup, and `reference` reads back upper-cased | Sold `F2-MVEHFK3QIQ` (`201`); GET `/policies/f2-mvehfk3qiq` → `200`, `reference` "F2-MVEHFK3QIQ", the sold policy. 2/2 `pm.test` Pass | PASS |
| 5 | GET-1 | GET-1 · amount 1000000.00 is written 1000000.00 → 201 | Send | `201`, `Location: /policies/{reference}`. In the 201 body and in the GET read-back, the raw text of `amount`, `terms[0].amount` and `payments[0].amount` is `1000000.00` each time, never `1000000.0` or `1000000` | POST `201`, `Location` /policies/F2-MVEHFSNYMH (asserted by the `pm.test`); 201 body and GET `200` read-back: `"amount":1000000.00` three times each (policy, `terms[0]`, `payments[0]`). 2/2 `pm.test` Pass | PASS |
| 6 | GET-1 | GET-1 · amount 803.40 is written 803.40 → 201 | Send | As step 5, each amount's raw text is `803.40` (the trailing zero is kept, never `803.4`) | POST `201` (`Location` asserted); 201 body and GET `200`: `"amount":803.40` ×3 each. 2/2 `pm.test` Pass | PASS |
| 7 | GET-1 | GET-1 · amount 100 is written 100.00 → 201 | Send (the body carries `100`, scale 0) | As step 5, each amount's raw text is `100.00` | Body sent `"amount": 100`; POST `201` (`Location` asserted); 201 body and GET `200`: `"amount":100.00` ×3 each. 2/2 `pm.test` Pass | PASS |
| 8 | GET-1 | GET-1 · amount 527.180 is written 527.18 → 201 | Send (the body carries `527.180`, scale 3, a whole number of pence) | As step 5, each amount's raw text is `527.18`, in the 201 body as well as the read-back (in F1b the 201 body echoed `527.180`) | Body sent `"amount": 527.180`; POST `201` (`Location` asserted); 201 body and GET `200`: `"amount":527.18` ×3 each, no `527.180`. 2/2 `pm.test` Pass | PASS |
| 9 | GET-1 | GET-1 · unknown reference → 404 | Send (folder `rule violations`) | `404`, `application/problem+json`, `type` /problems/get-1, `title` "Policy not found", `status` 404, `detail` "No policy with reference '{reference}'.", `instance` /policies/{reference}, `ruleId` GET-1, `reference`, a `traceId`, no `violations` | GET `/policies/F2-MVEHGMI5N2` → `404` `application/problem+json`: `type` /problems/get-1, `title` "Policy not found", `status` 404, `detail` "No policy with reference 'F2-MVEHGMI5N2'.", `instance` /policies/F2-MVEHGMI5N2, `ruleId` GET-1, `reference` F2-MVEHGMI5N2, `traceId` present, no `violations`. 1/1 `pm.test` Pass | PASS |
| 10 | GET-1 | GET-1 · unknown lower-case reference is named upper-cased → 404 | Send | As step 9, but `detail` and `reference` name the reference upper-cased, and `instance` is the path as sent, in lower case | GET `/policies/f2-mvehgollli` → `404` /problems/get-1: `detail` "No policy with reference 'F2-MVEHGOLLLI'.", `reference` F2-MVEHGOLLLI, `instance` /policies/f2-mvehgollli, `ruleId` GET-1, `traceId`, no `violations`. 1/1 `pm.test` Pass | PASS |
| 11 | GET-1 | GET-1 · reference failing the SELL-1 pattern → 404 | Send (the reference with `_X` appended) | As step 9, naming `{reference}_X`: a reference that fails the SELL-1 pattern is the 404 on a GET, not a 400, because no such policy can exist | GET `/policies/F2-MVEHGQN7ZO_X` → `404` /problems/get-1, `detail` "No policy with reference 'F2-MVEHGQN7ZO_X'.", `instance` /policies/F2-MVEHGQN7ZO_X, `ruleId` GET-1, `reference` F2-MVEHGQN7ZO_X, `traceId`, no `violations`; not a 400. 1/1 `pm.test` Pass | PASS |
| 12 | SHAPE | SHAPE · unknown query parameter on GET → 400 | Send (`?colour=red` on a policy it has just sold) | `400`, `title` "One or more validation errors occurred.", `instance` /policies/{reference}, `errors.colour` = ["Unknown query parameter."], no `ruleId`. The shape check runs before the lookup, even for a policy that exists | Sold `F2-MVEHGX8DHV` (`201`); GET `/policies/F2-MVEHGX8DHV?colour=red` → `400`: `title` "One or more validation errors occurred.", `instance` /policies/F2-MVEHGX8DHV, `errors` {"colour":["Unknown query parameter."]}, `traceId`, no `ruleId`. 2/2 `pm.test` Pass | PASS |
| 13 | SELL-7 | SELL-7 · amount -1e27 → 422 | Send | `422` /problems/sell-7, `title` "Policy amount must be greater than zero", `ruleId` SELL-7, `instance` /policies, a `traceId`, `violations` = [SELL-7], and the raw body contains exactly `"amount":-1000000000000000000000000000.00` followed by `,` or `}`. GET → `404` | POST → `422`: `type` /problems/sell-7, `title` "Policy amount must be greater than zero", `ruleId` SELL-7, `instance` /policies, `traceId`, `violations` [{SELL-7}]; raw body has `"amount":-1000000000000000000000000000.00,`. GET `/policies/F2-MVEHGZCI4R` → `404`. 2/2 `pm.test` Pass | PASS |
| 14 | SELL-7 | SELL-7 · amount decimal.MinValue → 422 | Send | As step 13, with the raw body containing exactly `"amount":-79228162514264337593543950335.00` followed by `,` or `}` (A3/R1: the old prefix match passed without `.00`). GET → `404` | POST → `422` /problems/sell-7, same `title`, `ruleId` SELL-7, `instance` /policies, `traceId`, `violations` [{SELL-7}]; raw body has `"amount":-79228162514264337593543950335.00,`. GET `/policies/F2-MVEHH1G92O` → `404`. 2/2 `pm.test` Pass | PASS |
| 15 | GET-1 | whole folder `F2 Retrieve` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F2 Retrieve"` | Every line is `Pass`. Requests 25 / 0 failed and assertions 26 / 0 failed: 14 requests in the folder (8 `happy path`, 6 `rule violations`) plus 11 sent from their scripts (4 pre-request sales, 4 money read-backs, 1 sale for the query row, 2 not-stored GETs). The closing "Postman credentials are required…" line is about cloud publishing only | 26 lines, every one `Pass`; summary requests 25 executed / 0 failed, assertions 26 executed / 0 failed | PASS |

### B. The F1 and F1b folders, unchanged and green (same API)

F2 adds the `F2 Retrieve` folder and changes no other request or assertion. The expected values for each F1 and F1b
request are the ones signed off in `docs/qa/build-F1.md` and `docs/qa/build-F1b.md`.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 16 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | whole folder `F1 Sell` | The step 15 command with `-i "F1 Sell"` | Every line is `Pass`. Requests 75 / 0 failed, assertions 73 / 0 failed: the totals F1 and F1b signed off | No `Fail` line; requests 75 executed / 0 failed, assertions 73 executed / 0 failed | PASS |
| 17 | SHAPE | whole folder `F1b Layered models` | The step 15 command with `-i "F1b Layered models"` | Every line is `Pass`. Requests 10 / 0 failed, assertions 10 / 0 failed: the totals F1b signed off | No `Fail` line; requests 10 executed / 0 failed, assertions 10 executed / 0 failed | PASS |
| 18 | GET-1 | — (the collection file) | `git diff --numstat main...HEAD -- tests/postman`, then `git diff -U0 main...HEAD -- tests/postman \| Select-String '^(-[^-]\|@@)'` | The first prints `868 0 tests/postman/Uinsure.Policies.postman_collection.json`: lines added, none removed. The second prints a single hunk header, `@@ -2657,0 +2658,868 @@`, and no removed line: the F2 folder is appended after the F1b folder, so no F0b, F1 or F1b line changed | First printed `868 0 tests/postman/Uinsure.Policies.postman_collection.json` (tab-separated); second printed the single line `@@ -2657,0 +2658,868 @@` and no removed line | PASS |
| 19 | GET-1, SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | the whole collection | The step 15 command without `-i` | Every line is `Pass`. Requests 112 / 0 failed, assertions 111 / 0 failed (`F0b` 2 + `F1 Sell` 75 + `F1b` 10 + `F2 Retrieve` 25 requests; 2 + 73 + 10 + 26 assertions) | No `Fail` line; requests 112 executed / 0 failed, assertions 111 executed / 0 failed | PASS |

### C. Hand requests: the rows Postman doesn't carry (same API; the shell setup and H1–H6 above)

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 20 | GET-1 | H1 | Run H1 (all three lines) | The POST is `201`. The first GET is `200`: `status` Pending, `currentTermNumber` 1, `amount` 527.18, `endDate` = start + 1 year − 1 day, `hasClaims` false, `refunds` and `claims` `[]`, `cancellation` null. The second GET is `404` /problems/get-1, `title` "Policy not found", `detail` "No policy with reference 'QA-F2-NEARX'.", `instance` /policies/QA-F2-NEARX, `ruleId` GET-1, `reference` QA-F2-NEARX: the lookup is exact, never a prefix match | POST `201`, `Location: /policies/QA-F2-NEAR`. GET QA-F2-NEAR `200`: `status` Pending, `currentTermNumber` 1, `amount` 527.18, start 2026-11-10, `endDate` 2027-11-09, `hasClaims` false, `refunds` `[]`, `claims` `[]`, `cancellation` null. GET QA-F2-NEARX `404`: `type` /problems/get-1, `title` "Policy not found", `detail` "No policy with reference 'QA-F2-NEARX'.", `instance` /policies/QA-F2-NEARX, `ruleId` GET-1, `reference` QA-F2-NEARX | PASS |
| 21 | SHAPE | H2 | Run H2 | `400`, `title` "One or more validation errors occurred.", `instance` /policies/QA-F2-NEAR, `errors.reference` = ["Unknown query parameter."], a `traceId` and no `ruleId`. Never the `200` of the policy (finding A2/I4) | `400` `application/problem+json`: `title` "One or more validation errors occurred.", `instance` /policies/QA-F2-NEAR, `errors` {"reference":["Unknown query parameter."]}, `traceId` present, no `ruleId` | PASS |
| 22 | SHAPE | H3 | Run H3 | As step 21, with `errors.cancellationToken` = ["Unknown query parameter."] | `400`, same `title`, `instance` /policies/QA-F2-NEAR, `errors` {"cancellationToken":["Unknown query parameter."]}, `traceId`, no `ruleId` | PASS |
| 23 | SHAPE | H4 | Run H4 (both lines) | The POST is `400`, `instance` /policies, `errors.request` = ["Unknown query parameter."], no `ruleId`, although the body is valid. The GET is `404`: nothing was stored | POST `/policies?request=x` → `400`, `instance` /policies, `errors` {"request":["Unknown query parameter."]}, no `ruleId`. GET QA-F2-QREQ → `404` /problems/get-1 | PASS |
| 24 | SELL-7 | H5 | Run H5 (both lines) | The POST is `422` /problems/sell-7, `ruleId` SELL-7, `violations` = [SELL-7], and the body text has `"amount":-1000000000000000000000000000.00`: the digits as sent, padded with `.00`, never a 500 and never rounded. The GET is `404` | POST → `422` /problems/sell-7, `ruleId` SELL-7, `violations` [{"ruleId":"SELL-7",…}], body has `"amount":-1000000000000000000000000000.00`. GET QA-F2-E27 → `404` | PASS |
| 25 | SELL-7 | H6 | Run H6 (both lines) | The POST is `422` /problems/sell-7, `ruleId` SELL-7, `violations` = [SELL-7], and the body text has `"amount":-79228162514264337593543950335.00`. The GET is `404` | POST → `422` /problems/sell-7, `ruleId` SELL-7, `violations` [{"ruleId":"SELL-7",…}], body has `"amount":-79228162514264337593543950335.00`. GET QA-F2-MIN → `404` | PASS |
| 26 | GET-1 | — (the API console) | Look at the API console output for sections A to C | No `fail:` or `crit:` line: nothing went down the 500 path (`UnexpectedExceptionHandler`, logged at Error). One `warn: Microsoft.EntityFrameworkCore.Query[20504]` line (more than one collection `Include`, single query) may appear at the first lookup after start-up; it is a performance hint, not a fault | Console (5934 lines, start-up to stop, covering A to C): 0 `fail:`, 0 `crit:`, 1 `warn:`, which is `Microsoft.EntityFrameworkCore.Query[20504]` (MultipleCollectionIncludeWarning, SingleQuery) at the first lookup. Everything else was `info:` (EF `Database.Command[20101]` ×603, `Hosting.Lifetime`) | PASS |

### D. Rows that need a fake clock or a seeded policy: covered by tests

Stop the API first (it locks the build output). A second term needs a renewal (F4), and `hasClaims` true needs a
claim (F3), so those policies are seeded through the app's `IPolicyRepository` (`PolicyBuilder`, `PolicySeeder`)
and read back over HTTP. Status boundaries need a `FakeTimeProvider`.

| # | Rule | Covered by test | Command | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 27 | GET-1 | `RetrieveStatusTests`: `Policy_sold_to_start_in_the_future_reads_back_Pending`, `Policy_sold_to_start_today_reads_back_Active`, `Status_on_the_day_before_the_start_is_Pending`, `Status_on_the_start_date_is_Active`, `Status_on_the_end_date_is_Active`, `Status_on_the_day_after_the_end_date_is_Expired`, and the three boundary rows of step 28. Each reads the policy back whole at the instant under test | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RetrieveStatusTests"` | 9 passed. A policy sold to start 2026-11-08 is Pending on 2026-11-07, Active on 2026-11-08 and on its end date 2027-11-07, and Expired on 2027-11-08 | `total: 9, failed: 0, succeeded: 9, skipped: 0`; all nine named methods are in `RetrieveStatusTests.cs` | PASS |
| 28 | GET-1 | The BST and GMT boundaries. Api.Tests: `Status_at_23_30_UTC_on_an_end_date_in_GMT_is_Active` (2027-11-07 23:30 UTC is still 7 November in the UK), `Status_at_23_30_UTC_on_an_end_date_in_BST_is_Expired_as_the_UK_date_is_the_next_day` (2027-10-16 23:30 UTC is 17 October in the UK, the day after the end), `Status_at_22_59_UTC_on_an_end_date_in_BST_is_Active` (23:59 UK time on the end date). Services.Tests: `Get_at_23_30_UTC_in_BST_on_the_UTC_date_of_the_end_returns_Expired`, `Get_at_23_30_UTC_in_BST_returns_the_term_starting_on_the_UK_date_as_current` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Status_at_23_30_UTC_on_an_end_date_in_GMT_is_Active" --filter-method "*.Status_at_23_30_UTC_on_an_end_date_in_BST_is_Expired_as_the_UK_date_is_the_next_day" --filter-method "*.Status_at_22_59_UTC_on_an_end_date_in_BST_is_Active"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-method "*.Get_at_23_30_UTC_in_BST_on_the_UTC_date_of_the_end_returns_Expired" --filter-method "*.Get_at_23_30_UTC_in_BST_returns_the_term_starting_on_the_UK_date_as_current"` | 3 passed, then 2 passed. Status is derived from the UK date, never the UTC date | Api.Tests `total: 3, failed: 0, succeeded: 3, skipped: 0`; then Services.Tests `total: 2, failed: 0, succeeded: 2, skipped: 0` | PASS |
| 29 | GET-1 | `RetrieveCurrentTermTests` (a seeded two-term policy: term 1 2026-03-02 to 2027-03-01, 486.20 by Card; term 2 2027-03-02 to 2028-03-01, 512.75 by DirectDebit): `Today_inside_the_first_of_two_terms_shows_the_first_term`, `Last_day_of_the_first_term_still_shows_the_first_term`, `First_day_of_the_second_term_shows_the_second_term`, `Day_after_the_latest_term_shows_the_latest_term_and_is_Expired`, `Unpaid_second_term_reads_back_with_no_payment_reference_and_no_payment` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RetrieveCurrentTermTests"` | 5 passed. The policy-level `startDate`, `endDate`, `amount` and `currentTermNumber` are the term covering UK today, else the latest term; `terms[]` holds both terms oldest first, numbered 1 and 2; `payments` holds both payments (one when term 2 is unpaid, with its `paymentReference` null). Every row asserts its `status` too, the unpaid row included (A2/I5); the canned term number that matched these rows at the stub is gone (A1/I2) | `total: 5, failed: 0, succeeded: 5, skipped: 0`; the five named methods are in `RetrieveCurrentTermTests.cs` | PASS |
| 30 | GET-1 | `RetrieveHasClaimsTests`: `Policy_stored_with_claims_reads_back_hasClaims_true_and_the_rest_as_stored` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RetrieveHasClaimsTests"` | 1 passed. A policy stored with `HasClaims` true reads back `hasClaims` true and every other value as stored (finding A2/I1: the green code had passed a literal false, and every sold row also expects false) | `total: 1, failed: 0, succeeded: 1, skipped: 0` | PASS |
| 31 | GET-1 | `PolicyMoneyFormatTests`: the four sale rows of steps 5–8 over HTTP, plus `Value_0_125_is_written_0_125_by_the_app_json_options_and_never_rounded` and `Value_803_4_is_padded_to_803_40_by_the_app_json_options`, which ask the app's own configured JSON options directly, because no request can store a value with more than 2 decimal places | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.PolicyMoneyFormatTests"` | 6 passed. Money is padded to 2 decimal places and never rounded: 0.125 is written `0.125`, so a missing `Money.Round` upstream shows in a test instead of being hidden by the writer (finding A2/I3) | `total: 6, failed: 0, succeeded: 6, skipped: 0` | PASS |
| 32 | SELL-7 | `Amount_of_minus_10_to_the_27_is_echoed_in_the_SELL_7_problem_as_its_exact_text_with_2_decimal_places`, `Amount_of_decimal_MinValue_is_echoed_in_the_SELL_7_problem_as_its_exact_text_with_2_decimal_places` (`SellAmountShapeTests`, traited SELL-7 and GET-1) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Amount_of_minus_10_to_the_27_is_echoed_in_the_SELL_7_problem_as_its_exact_text_with_2_decimal_places" --filter-method "*.Amount_of_decimal_MinValue_is_echoed_in_the_SELL_7_problem_as_its_exact_text_with_2_decimal_places"` | 2 passed. The raw `amount` token is exactly `-1000000000000000000000000000.00` and `-79228162514264337593543950335.00`, and nothing is stored (A2/I3, A3/R1) | `total: 2, failed: 0, succeeded: 2, skipped: 0` | PASS |
| 33 | SHAPE | `Unknown_query_parameter_on_a_get_is_rejected_with_400`, `Query_key_named_like_the_route_reference_on_a_get_is_rejected_with_400`, `Query_key_named_like_the_cancellation_token_on_a_get_is_rejected_with_400` (`RetrieveNotFoundTests`), `Query_key_named_like_the_body_parameter_on_a_sale_is_rejected_with_400` (`SellShapeTests`). Untraited: shape is not a numbered rule | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Unknown_query_parameter_on_a_get_is_rejected_with_400" --filter-method "*.Query_key_named_like_the_route_reference_on_a_get_is_rejected_with_400" --filter-method "*.Query_key_named_like_the_cancellation_token_on_a_get_is_rejected_with_400" --filter-method "*.Query_key_named_like_the_body_parameter_on_a_sale_is_rejected_with_400"` | 4 passed. `?colour`, `?reference`, `?cancellationToken` and `?request` each answer 400 with `errors[key]` = ["Unknown query parameter."] and no `ruleId`; the sale with `?request` is not stored (A2/I4) | `total: 4, failed: 0, succeeded: 4, skipped: 0` | PASS |
| 34 | GET-1 | `RetrieveNotFoundTests` (the four 404 rows: unknown, lower-case, pattern-failing, one character longer, plus the three query rows of step 33) and `Get_of_a_reference_with_no_policy_fails_with_one_GET_1_NotFound_violation` (`PolicyServiceGetDetailsTests`) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RetrieveNotFoundTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-method "*.Get_of_a_reference_with_no_policy_fails_with_one_GET_1_NotFound_violation"` | 7 passed, then 1 passed. Over HTTP each 404 is `/problems/get-1` with `detail` and `reference` naming the normalised reference and no `violations`. In the service, the result fails with one violation: `GET-1`, `NotFound`, the same detail and a `reference` extension (A2/I6) | Api.Tests `total: 7, failed: 0, succeeded: 7, skipped: 0`; then Services.Tests `total: 1, failed: 0, succeeded: 1, skipped: 0` | PASS |
| 35 | GET-1 | `RetrieveResponseShapeTests`: `Sold_policy_reads_back_in_full_with_status_current_term_and_empty_history`, `Sale_201_body_is_the_full_policy_response_and_equals_the_read_back`, `Lower_case_route_reference_with_spaces_reads_back_the_policy_in_full` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RetrieveResponseShapeTests"` | 3 passed. The 201 body of a sale is the same `PolicyResponse` as the GET, byte for byte; ` get1-lower ` (lower case, with spaces) finds `GET1-LOWER` | `total: 3, failed: 0, succeeded: 3, skipped: 0`; the three named methods are in `RetrieveResponseShapeTests.cs` | PASS |
| 36 | GET-1 | `PolicyTermsTests` and `PolicyStatusTests` (Domain.Tests, `DateOnly today` passed in): `LatestTerm`, `CurrentTerm(today)` and `StatusOn(today)` on each side of each term boundary, for one-term and two-term policies. Traited GET-1 in F2 | `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.PolicyTermsTests" --filter-class "Uinsure.Policies.Domain.Tests.PolicyStatusTests"` | 22 passed | `total: 22, failed: 0, succeeded: 22, skipped: 0` | PASS |
| 37 | GET-1 | `PolicyServiceGetDetailsTests` (Services.Tests): Pending, Expired, the two-term current term, the two BST rows of step 28 and the not-found row of step 34 | `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceGetDetailsTests"` | 6 passed | `total: 6, failed: 0, succeeded: 6, skipped: 0` | PASS |
| 38 | GET-1 | every test traited GET-1, across Domain, Services and Api tests | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=GET-1"` | 59 passed, 0 failed, 0 skipped: Api.Tests 30, Domain.Tests 22, Services.Tests 7 | `total: 59, failed: 0, succeeded: 59, skipped: 0`. Per project with the same trait filter: Api.Tests 30, Domain.Tests 22, Services.Tests 7 (all succeeded) | PASS |
| 39 | SELL-7 | every test traited SELL-7 | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-7"` | 15 passed, 0 failed, 0 skipped. F1b signed off 13; the two exact-echo rows of step 32 are the only additions | `total: 15, failed: 0, succeeded: 15, skipped: 0` | PASS |
| 40 | GET-1 | `RuleCoverageTests` | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`, then open `tests/rule-coverage.pending.txt` | 11 passed. The pending file has 22 IDs and no GET-1 (F1b signed off 23: F2 took GET-1 out) | `total: 11, failed: 0, succeeded: 11, skipped: 0`. The pending file has 22 IDs (CANCEL-1a to CANCEL-7, RENEW-1 to RENEW-6 with 3a/3b, COULD-1 to COULD-3 with 2a/2b/2c) and no GET-1 | PASS |
| 41 | GET-1 | the whole suite | `dotnet test --solution Uinsure.Policies.slnx` | 374 passed, 0 failed, 0 skipped (F1b signed off at 339) | Re-run after section C: `total: 374, failed: 0, succeeded: 374, skipped: 0` (the precondition run gave the same) | PASS |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- How these expectations were checked: on 2026-10-18 at about 23:36 UTC (2026-10-19 00:36 UK time, BST) the tester
  ran, at HEAD 4772bc9 (code identical to 143e5ba): the full suite (374 / 0 / 0) and every command in section D, with
  the counts given; the API from the worktree (`Hosting environment: Development`, 5080 and 7080); the Postman CLI
  on `F2 Retrieve` (25 / 0 requests, 26 / 0 assertions), `F1 Sell` (75 / 0, 73 / 0), `F1b Layered models` (10 / 0,
  10 / 0) and the whole collection (112 / 0, 111 / 0); and step 18's diff. The hand requests were sent with Git Bash
  `curl` as `QAV-F2-*`, not this script's `QA-F2-*`, so your references stay free. They gave the statuses and
  bodies in section C. The console showed 0 `fail:` and 0 `crit:` lines, and one `warn:` (EF Core 20504). The
  harness doesn't let the tester run `powershell.exe`, so the shell setup and the `Sell` function have not been
  run as written. If one misbehaves, note it here: that is a script defect, not an API defect. The Actual and
  Pass/Fail columns are yours.
- For your ruling, not raised as a finding: a query key that differs only in case from a route parameter is rejected
  as well (`?REFERENCE=X` → 400, as the case-insensitive contract requires), but its `errors` key comes back as
  `reference`, not `REFERENCE`. That looks like the camelCase dictionary-key policy of the JSON options, not the
  filter. It was seen once by hand and no test covers it.
- Run Postman and the hand steps away from UK midnight if you can. Both compute UK today themselves, as the API
  does, so a run that crosses midnight can sell on one date and read back on the next.
- Execution at beba48e (tester, run for David, 2026-10-19 00:52 to 01:02 UK time): 41 of 41 step rows PASS and 0 FAIL,
  so no defect is listed. The run started about 52 minutes after UK midnight and finished well before the next one,
  so no sale and read-back crossed a UK date. The deviations above concern how the steps were run, not what they
  expect. The two that bear on the evidence: the shell setup and `Sell` were expanded by hand in Git Bash, so the
  PowerShell text itself is still unexercised (as the note above says); and the stale `-wal`/`-shm` files were not
  deleted. The second is not a defect, because `CREATE TABLE` at start-up and the five `404`s show the database was
  fresh. Not checked by any step, and not run: the `?REFERENCE=X` errors-key casing raised in the note above is still
  for your ruling.

## Sign-off

| | |
|---|---|
| Tester | David Morgan (steps run by the tester agent, verified by the orchestrator) |
| Date | 2026-10-19 |
| Commit signed off | beba48e3f7f4efc948d3542dffce87bfe2de33df (PR #17 head; `src/` and `tests/` identical to 143e5ba) |
| `dotnet test` result | 374 passed / 0 failed / 0 skipped (`dotnet test --solution Uinsure.Policies.slnx`, run before and after the API session) |
| Postman CLI result | `F2 Retrieve` 25 / 0 requests, 26 / 0 assertions; `F1 Sell` 75 / 0, 73 / 0; `F1b Layered models` 10 / 0, 10 / 0; whole collection 112 / 0, 111 / 0 (Postman CLI 1.75.0, `local` environment) |
| Steps passed | 41 of 41 |
| Defects raised (finding ids) | None in F2. `?REFERENCE=X` is correctly a 400, but its errors key is written `reference` (camelCase naming policy); cosmetic, carried over to F3 |
| Verdict | ☐ Reject (findings above)  ☑ Accept F2 |
| Signature | David Morgan 19/10/2026 01:05 |
