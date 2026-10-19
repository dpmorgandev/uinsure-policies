# QA script — build / F3 — Cancel, with the quote and claims

Rule IDs: CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c
Branch: feature/build/F3   Worktree: .claude/worktrees/build-F3
Commit under test: a59fb6c (the last code or test commit; both A2.3 verdicts are PASS at it. 520626a after it changes only `docs/plans/active/build.dag.yaml`, and this script's own commit adds only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080   Environment: local (`tests/postman/local.postman_environment.json`)

F3 adds three routes. `POST /policies/{reference}/cancellation` (E3) cancels a policy and answers 201 with the
`CancellationResponse` and no `Location`. `GET /policies/{reference}/cancellation-quote?cancellationDate=` (E4) answers
200 with the same calculation plus `quotedOn`, and changes nothing. `POST /policies/{reference}/claims` (E5) records a
claim and answers 201 with `Location: /policies/{reference}` and the `ClaimResponse`. The refund is full before the
start (CANCEL-1a) and up to start + 14 inclusive (CANCEL-1b). After that it is pro-rata over the term's actual days,
with the cancellation day counted as used (CANCEL-1c), rounded once to 2 dp, half away from zero (CANCEL-6). It goes
back to the method of the payment it reverses (CANCEL-2). Any claim gives a £0 refund (COULD-2), and no £0 Refund
record is ever made. A cancelled policy reads back `status` Cancelled, with its `cancellation` and `refunds`.

The Rule column holds a bare rule id. A row tagged with several ids lists them comma-separated. `GET-1` marks the 404
rows of the three new routes. `CONCURRENCY` marks the untraited concurrency rows: the contract's `/problems/concurrency`
409 guards no numbered rule, like F2's `SHAPE`. The 400 shape rows carry the rule of the input they guard, as the
Postman names and the test traits do: `CANCEL-3` for the cancel body, `COULD-1` for the quote's query, `COULD-2` for the
claim body. Finding ids from `docs/trace/build/F3/findings/A1.md` are cited as `A1/I1` and `A1/I2`. Reviewer and
security findings in `docs/trace/build/F3/verdicts/` are cited as `A2/R1` to `A2/R4`, `A2/S1`, `A2.3/R1` and `A2.3/S1`.

Commands are for PowerShell, run from the worktree root. Inside the tables, `\|` is a Markdown escape: type a
plain `|` (the PowerShell pipe).

**Dates and amounts.** In the expected values, `T` is UK today (Europe/London) and `E` is `T` + 1 year − 1 day, the end
of a policy that starts on `T`. Postman and the hand steps compute both from the real clock, as the API does. Every term
in sections A and C is 365 days long while `T` is on or before 2027-02-06, because no term sold by then contains a
29 February. So the day counts and refund amounts below are fixed literals; only the dates move with `T`. Unless a row
says otherwise, each Postman sale is the F3 default: Household, auto-renew on, Katherine Johnson (born 1981-08-26),
48 Quay St, Unit 5, M3 4LZ.

## Preconditions

- [ ] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: __ / __ / __). Expected: 528 / 0 / 0.
- [ ] Ports 5080 and 7080 are free (`netstat -ano | findstr ":5080 :7080"` prints no LISTENING line). A running API also locks the build output, so stop it before any `dotnet build` or `dotnet test`.
- [ ] The fixed `QA-F3-*` references of the hand steps are free. Either start from an empty database (with the API stopped: `Remove-Item src/Uinsure.Policies.Api/policies.development.db*`; start-up recreates it with `EnsureCreated`), or, once the API is up, check that a GET of each of `QA-F3-REFS`, `QA-F3-EMPTY`, `QA-F3-TWICE`, `QA-F3-CLAIM` and `QA-F3-SHAPE` gives `404`.
- [ ] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).
- [ ] You are running well away from UK midnight. A sale and its cancel or claim must fall on the same UK date (see the notes).

### Starting the API

`dotnet run --project src/Uinsure.Policies.Api`. The console shows `Now listening on: https://localhost:7080`,
`Now listening on: http://localhost:5080` and `Hosting environment: Development`. Keep the console in view: step 49
reads it.

### Shell setup for the hand steps (H1–H9)

Paste this once into the PowerShell window you use for the hand steps. UK today comes from Europe/London, as in the
API. `Day n` is UK today + n days, as `yyyy-MM-dd`.

```powershell
$ukToday = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'GMT Standard Time').Date
function Day([int]$offset) { $ukToday.AddDays($offset).ToString('yyyy-MM-dd') }
function Sell([string]$reference, [string]$start, [string]$amount, [string]$method) {
  ('{"reference":"' + $reference + '","type":"Household","startDate":"' + $start + '","amount":' + $amount + ',"autoRenew":true,"paymentMethod":"' + $method + '","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}') |
    curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-"
}
function PostTo([string]$path, [string]$json) {
  $json | curl.exe -s -i -X POST ("http://localhost:5080/policies/" + $path) -H "Content-Type: application/json" --data-binary "@-"
}
function GetFrom([string]$path) { curl.exe -s -i ("http://localhost:5080/policies/" + $path) }
```

The JSON is piped to curl so that PowerShell does not strip its quotes. Don't pipe an empty string to send "no body":
PowerShell adds a line break, and a body that is only a line break is not an empty body (see the notes). H1 and H2
send no body by leaving out `--data-binary`.

### Hand requests

```powershell
# H1 [CANCEL-2] the refund and payment references: no body, before a future start
Sell 'QA-F3-REFS' (Day 22) '512.34' 'Card'
curl.exe -s -i -X POST http://localhost:5080/policies/QA-F3-REFS/cancellation
GetFrom 'QA-F3-REFS'

# H2 [CANCEL-3] a JSON content type with no body at all means UK today
Sell 'QA-F3-EMPTY' (Day 0) '276.15' 'DirectDebit'
curl.exe -s -i -X POST http://localhost:5080/policies/QA-F3-EMPTY/cancellation -H "Content-Type: application/json"

# H3 [CANCEL-4] cancel twice, the second time with yesterday's date: the 409 comes before the 422 CANCEL-3
Sell 'QA-F3-TWICE' (Day 0) '401.20' 'DirectDebit'
PostTo 'QA-F3-TWICE/cancellation' ('{"cancellationDate":"' + (Day 0) + '"}')
PostTo 'QA-F3-TWICE/cancellation' ('{"cancellationDate":"' + (Day -1) + '"}')

# H4 [CANCEL-4] quote the cancelled policy for yesterday: the 409 comes before the 422 CANCEL-3
GetFrom ('QA-F3-TWICE/cancellation-quote?cancellationDate=' + (Day -1))

# H5 [COULD-2c] claim tomorrow on the cancelled policy: the 409 comes before the 422 COULD-2a
PostTo 'QA-F3-TWICE/claims' ('{"claimDate":"' + (Day 1) + '"}')
GetFrom 'QA-F3-TWICE'

# H6 [COULD-2] the claim body shapes Postman doesn't carry; nothing is recorded
Sell 'QA-F3-CLAIM' (Day 0) '344.44' 'Card'
PostTo 'QA-F3-CLAIM/claims' '{"claimDate":"19/10/2026"}'
PostTo 'QA-F3-CLAIM/claims' ('{"claimDate":"' + (Day 0) + '","severity":"High"}')
PostTo 'QA-F3-CLAIM/claims' '{"claimDate":'
PostTo 'QA-F3-CLAIM/claims' '{}'
GetFrom 'QA-F3-CLAIM'

# H7 [COULD-2] two claims (500-character description, then none), then quote and cancel after cooling-off: £0
PostTo 'QA-F3-CLAIM/claims' ('{"claimDate":"' + (Day 0) + '","description":"' + ('x' * 500) + '"}')
PostTo 'QA-F3-CLAIM/claims' ('{"claimDate":"' + (Day 0) + '"}')
GetFrom ('QA-F3-CLAIM/cancellation-quote?cancellationDate=' + (Day 15))
PostTo 'QA-F3-CLAIM/cancellation' ('{"cancellationDate":"' + (Day 15) + '"}')
GetFrom 'QA-F3-CLAIM'

# H8 [CANCEL-3] the cancel body shapes Postman doesn't carry
Sell 'QA-F3-SHAPE' (Day 22) '299.95' 'Cheque'
PostTo 'QA-F3-SHAPE/cancellation' '{"cancellationDate":"2026/11/01"}'
PostTo 'QA-F3-SHAPE/cancellation' '{"cancellationDate":'

# H9 [COULD-1] the quote query shapes Postman doesn't carry
GetFrom 'QA-F3-SHAPE/cancellation-quote?cancellationDate=2027-02-30'
GetFrom 'QA-F3-SHAPE/cancellation-quote?cancellationDate=18/11/2026'
GetFrom ('QA-F3-SHAPE/cancellation-quote?cancellationDate=' + (Day 30) + '&reference=OTHER-REF')
GetFrom 'QA-F3-SHAPE'
```

## Steps

### A. The F3 Cancel folder (API running in Development, from "Starting the API")

Send each request from the folder `F3 Cancel` in the Postman app, or run it alone with
`postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "<request name>" --verbose`.
Each request makes its own `F3-…` reference. Its pre-request script sells the policy (and, where stated, first claims
on or cancels it), and its first `pm.test` checks that every setup call answered as listed. "The cancellation shape"
means the body has exactly `reference`, `cancellationDate`, `ruleApplied`, `totalRefund`, `terms` and `refunds` (a
quote adds `quotedOn`, and its refunds have no `reference`). Each term has exactly `termNumber`, `ruleApplied`,
`daysInTerm`, `daysUsed`, `unusedDays` and `refundAmount`. Each refund has `reference`, `type`, `amount` and
`paymentReference`. Money is checked as the raw response text, with exactly 2 decimal places.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | CANCEL-1a, CANCEL-3 | CANCEL-1a · cancel with no body before a future start → 201 | Send (`happy path`). Setup: sell start T + 22, 527.18 by DirectDebit. The cancel has no body | `201`, no `Location`, the cancellation shape: `cancellationDate` T, `ruleApplied` CANCEL-1a, `terms` = [{1, CANCEL-1a, 365, 0, 365, 527.18}], `totalRefund` 527.18, one refund (DirectDebit, 527.18, `paymentReference` = the sale's payment reference, its own non-empty `reference`). The read-back GET is `200`, `status` Cancelled (not Pending), `cancellation` {T, CANCEL-1a, 527.18}, `refunds` = that refund, `hasClaims` false, `claims` `[]`. 3 `pm.test` | | |
| 2 | CANCEL-1b | CANCEL-1b · cancel on the start date → 201 | Send. Setup: sell start T, 289.40 by Cheque; cancel `{"cancellationDate": T}` | `201`, no `Location`; `ruleApplied` CANCEL-1b, `terms` = [{1, CANCEL-1b, 365, 1, 364, 289.40}], `totalRefund` 289.40, one refund to Cheque for 289.40 reversing the sale's payment. Read-back `status` Cancelled, `cancellation` {T, CANCEL-1b, 289.40}, the refund stored. 3 `pm.test` | | |
| 3 | CANCEL-1b | CANCEL-1b · cancel on start+14 → 201 | Send. Setup: sell start T, 604.25 by DirectDebit; cancel on T + 14 (the last day of cooling-off) | `201`; `cancellationDate` T + 14, `ruleApplied` CANCEL-1b, `terms` = [{1, CANCEL-1b, 365, 15, 350, 604.25}], `totalRefund` 604.25 (still in full), one refund to DirectDebit for 604.25. 2 `pm.test` | | |
| 4 | CANCEL-1c | CANCEL-1c · cancel on start+15 → 201 | Send. Setup: sell start T, 527.18 by DirectDebit; cancel on T + 15 (the first day after cooling-off) | `201`; `ruleApplied` CANCEL-1c, `terms` = [{1, CANCEL-1c, 365, 16, 349, 504.07}] (527.18 × 349 ÷ 365 = 504.0707…), `totalRefund` 504.07, one refund to DirectDebit for 504.07. Read-back `status` Cancelled, `cancellation` {T + 15, CANCEL-1c, 504.07}, the refund stored. 3 `pm.test` | | |
| 5 | CANCEL-2, CANCEL-1a | CANCEL-2 · refund goes to the Card payment it reverses → 201 | Send. Setup: sell start T + 22, 318.62 by Card; cancel on T + 21 (the day before the start) | `201`; `ruleApplied` CANCEL-1a, `terms` = [{1, CANCEL-1a, 365, 0, 365, 318.62}], one refund: `type` Card, 318.62, `paymentReference` = the Card payment's reference. Read-back `refunds` = the same single refund. 3 `pm.test` | | |
| 6 | CANCEL-3 | CANCEL-3 · empty object means today → 201 | Send. Setup: sell start T + 22, 250.50 by DirectDebit; the body is `{}` | `201`; `cancellationDate` T, `ruleApplied` CANCEL-1a, `terms` = [{1, CANCEL-1a, 365, 0, 365, 250.50}], one refund to DirectDebit for 250.50. 2 `pm.test` | | |
| 7 | CANCEL-3 | CANCEL-3 · null cancellationDate means today → 201 | Send. Setup: sell start T, 412.66 by Cheque; the body is `{"cancellationDate": null}` | `201`; `cancellationDate` T, `ruleApplied` CANCEL-1b, `terms` = [{1, CANCEL-1b, 365, 1, 364, 412.66}], one refund to Cheque for 412.66. 2 `pm.test` | | |
| 8 | CANCEL-6, CANCEL-1c | CANCEL-6 · pro-rata refund rounded once to 2 dp, half away from zero → 201 | Send. Setup: sell start T, 687.45 by Card; cancel on T + 15 | `201`; `ruleApplied` CANCEL-1c, `terms` = [{1, CANCEL-1c, 365, 16, 349, 657.32}] (687.45 × 349 ÷ 365 = 657.3152…, so rounded, not truncated to 657.31), one refund to Card for 657.32. Every `refundAmount`, `totalRefund` and `amount` in the raw body has exactly 2 decimal places. 3 `pm.test`. The exact half-penny is step 51 | | |
| 9 | CANCEL-7, CANCEL-1c | CANCEL-7 · cancel on the last day of cover → 201 | Send. Setup: sell start T, 365.73 by DirectDebit; cancel on E | `201`; `cancellationDate` E, `ruleApplied` CANCEL-1c, `terms` = [{1, CANCEL-1c, 365, 365, 0, 0.00}], `totalRefund` 0.00 (raw text `0.00`), `refunds` `[]` (no £0 Refund record). Read-back `status` Cancelled, `cancellation` {E, CANCEL-1c, 0.00}, `refunds` `[]`. 3 `pm.test` | | |
| 10 | COULD-1, CANCEL-1c | COULD-1 · quote then cancel for the same date agree → 201 | Send. Setup: sell start T, 615.40 by DirectDebit; quote for T + 100 (`200`). Then cancel on T + 100 | `201`; `ruleApplied` CANCEL-1c, `terms` = [{1, CANCEL-1c, 365, 101, 264, 445.11}] (615.40 × 264 ÷ 365 = 445.1112…), one refund to DirectDebit for 445.11. The earlier quote has the quote shape, `quotedOn` T, and the same `cancellationDate`, `ruleApplied`, `totalRefund` and `terms`, and the same refunds less their `reference`. 3 `pm.test` | | |
| 11 | COULD-1, CANCEL-1c | COULD-1 · quote changes nothing → 200 | Send. Setup: sell start T + 22, 384.26 by Card, then GET it (`200`, kept as text). The quote is for T + 44 | `200`, the quote shape: `quotedOn` T, `cancellationDate` T + 44, `ruleApplied` CANCEL-1c, `terms` = [{1, CANCEL-1c, 365, 23, 342, 360.05}], one refund (Card, 360.05, the sale's payment reference) with no `reference`. A GET afterwards is byte for byte the GET before, with `cancellation` null. 3 `pm.test` | | |
| 12 | COULD-2 | COULD-2 · claim dated today → 201 | Send. Setup: sell start T, 389.17 by Card. Body `{"claimDate": T, "description": "Escape of water from the boiler"}` | `201`, `Location` /policies/{reference}. The body has exactly `reference` (non-empty), `policyReference` {reference}, `claimDate` T, `description` as sent, `hasClaims` true. Read-back `hasClaims` true, `status` Active, `claims` = [that claim's `reference`, T, the description], `cancellation` null. 3 `pm.test` | | |
| 13 | COULD-2 | COULD-2 · claim then cancel refunds nothing → 201 | Send. Setup: sell start T, 441.90 by Card; claim dated T (`201`). The cancel has no body | `201`; `cancellationDate` T, `ruleApplied` COULD-2 (CANCEL-1b would refund 441.90), `terms` = [{1, COULD-2, 365, 1, 364, 0.00}], `totalRefund` 0.00, `refunds` `[]`. Read-back `status` Cancelled, `hasClaims` true, one claim, `cancellation` {T, COULD-2, 0.00}, `refunds` `[]`. 3 `pm.test` | | |
| 14 | COULD-2, COULD-1 | COULD-2 · quote for a policy with a claim quotes nothing → 200 | Send. Setup: sell start T, 476.25 by Card; claim dated T. The quote is for T + 3 | `200`, the quote shape: `quotedOn` T, `ruleApplied` COULD-2, `terms` = [{1, COULD-2, 365, 4, 361, 0.00}], `totalRefund` 0.00, `refunds` `[]`. 2 `pm.test` | | |
| 15 | CANCEL-3 | CANCEL-3 · cancellation date yesterday → 422 | Send (`rule violations`). Setup: sell start T, 333.80 by Card; cancel on T − 1 | `422` `application/problem+json`: `type` /problems/cancel-3, `title` "Cancellation date in the past", `instance` /policies/{reference}/cancellation, `ruleId` CANCEL-3, `violations` = [CANCEL-3], `today` T, a `traceId`. Read-back: `cancellation` null, `hasClaims` false, `claims` and `refunds` `[]`. 3 `pm.test` | | |
| 16 | CANCEL-3, COULD-1 | CANCEL-3 · quote dated yesterday → 422 | Send. Setup: sell start T, 341.20 by DirectDebit; quote for T − 1 | As step 15, with `instance` /policies/{reference}/cancellation-quote. 2 `pm.test` | | |
| 17 | CANCEL-3 | CANCEL-3 · unknown field in the cancellation body → 400 | Send. Setup: sell start T + 22; body `{"cancellationDate": T, "reason": "Moving abroad"}` | `400`, `title` "One or more validation errors occurred.", `instance` /policies/{reference}/cancellation, `errors` has `$.reason`, no `ruleId`. Read-back unchanged. 3 `pm.test` | | |
| 18 | CANCEL-4 | CANCEL-4 · cancel twice → 409 | Send. Setup: sell start T, 458.12 by Card; cancel on T + 30 (`201`). This cancel is for T + 40 | `409` /problems/cancel-4, `title` "Policy already cancelled", `instance` /policies/{reference}/cancellation, `ruleId` CANCEL-4, `cancellationDate` T + 30 (the original), `detail` naming T + 30, no `violations`, a `traceId`. 2 `pm.test` | | |
| 19 | CANCEL-4, COULD-1 | CANCEL-4 · quote a cancelled policy → 409 | Send. Setup: sell start T, 318.90 by Cheque; cancel on T + 45. The quote is for T + 80 | `409` /problems/cancel-4, `instance` /policies/{reference}/cancellation-quote, `ruleId` CANCEL-4, `cancellationDate` T + 45, no `violations`. 2 `pm.test` | | |
| 20 | CANCEL-7 | CANCEL-7 · cancellation date the day after the end → 422 | Send. Setup: sell start T, 377.77 by Card; cancel on E + 1 | `422` /problems/cancel-7, `title` "Cancellation date after the end of cover", `instance` /policies/{reference}/cancellation, `ruleId` CANCEL-7, `violations` = [CANCEL-7], `latestEndDate` E. Read-back unchanged. 3 `pm.test` | | |
| 21 | CANCEL-7, COULD-1 | CANCEL-7 · quote the day after the end → 422 | Send. Setup: sell start T + 22, 296.85 by Cheque; quote for its end + 1 | `422` /problems/cancel-7, `instance` /policies/{reference}/cancellation-quote, `violations` = [CANCEL-7], `latestEndDate` = T + 22 + 1 year − 1 day. 2 `pm.test` | | |
| 22 | COULD-1 | COULD-1 · quote without cancellationDate → 400 | Send (no query string) | `400`, `instance` /policies/{reference}/cancellation-quote, `errors` has `cancellationDate`, no `ruleId`. 2 `pm.test` | | |
| 23 | COULD-1 | COULD-1 · unknown query parameter on the quote → 400 | Send (`?cancellationDate=T+30&cancelationDate=T+30`, the second misspelt) | `400`, `errors` has `cancelationDate`, no `ruleId`. 2 `pm.test` | | |
| 24 | COULD-2 | COULD-2 · claim without claimDate → 400 | Send. Setup: sell start T, 402.05 by DirectDebit; body `{"description": "Water damage under the kitchen sink"}` | `400` (never `500`: finding A1/I1), `instance` /policies/{reference}/claims, `errors` has `claimDate`, no `ruleId`. Read-back: `hasClaims` false, `claims` `[]`. 3 `pm.test` | | |
| 25 | COULD-2 | COULD-2 · description of 501 characters → 400 | Send. Setup: sell start T, 276.48 by DirectDebit; `description` is 501 `x` | `400` (never `500`, A1/I1), `errors` has `description`, no `ruleId`. Read-back unchanged. 3 `pm.test` | | |
| 26 | COULD-2a | COULD-2a · claim dated tomorrow → 422 | Send. Setup: sell start T, 367.21 by Card; claim dated T + 1 | `422` /problems/could-2a, `title` "Claim date in the future", `instance` /policies/{reference}/claims, `ruleId` COULD-2a, `violations` = [COULD-2a], `today` T. Read-back unchanged. 3 `pm.test` | | |
| 27 | COULD-2a, COULD-2b | COULD-2a · claim dated after today and after the end → 422 | Send. Setup: sell start T, 394.72 by Card; claim dated E + 16 | `422` /problems/could-2a, `ruleId` COULD-2a, `detail` "2 rules failed.", `violations` = [COULD-2a, COULD-2b] in that order. Read-back unchanged. 3 `pm.test` | | |
| 28 | COULD-2b | COULD-2b · claim before a future start → 422 | Send. Setup: sell start T + 22, 261.94 by DirectDebit; claim dated T | `422` /problems/could-2b, `title` "Claim date outside cover", `ruleId` COULD-2b, `violations` = [COULD-2b], `coverStart` T + 22, `coverEnd` its end. Read-back unchanged. 3 `pm.test` | | |
| 29 | COULD-2b | COULD-2b · claim dated yesterday on a policy starting today → 422 | Send. Setup: sell start T, 425.30 by Cheque; claim dated T − 1 | `422` /problems/could-2b, `violations` = [COULD-2b], `coverStart` T, `coverEnd` E. Read-back unchanged. 3 `pm.test` | | |
| 30 | COULD-2c | COULD-2c · claim on a cancelled policy → 409 | Send. Setup: sell start T, 433.25 by DirectDebit; cancel on T + 59. The claim is dated T | `409` /problems/could-2c, `title` "Claim on a cancelled policy", `instance` /policies/{reference}/claims, `ruleId` COULD-2c, `cancellationDate` T + 59, `detail` naming T + 59, no `violations`. 2 `pm.test` | | |
| 31 | GET-1 | GET-1 · cancel an unknown policy → 404 | Send (nothing sold) | `404` /problems/get-1, `title` "Policy not found", `instance` /policies/{reference}/cancellation, `ruleId` GET-1, `reference` {reference}, no `violations`. 1 `pm.test` | | |
| 32 | GET-1 | GET-1 · quote an unknown policy → 404 | Send | As step 31, with `instance` /policies/{reference}/cancellation-quote | | |
| 33 | GET-1 | GET-1 · claim on an unknown policy → 404 | Send | As step 31, with `instance` /policies/{reference}/claims | | |
| 34 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | whole folder `F3 Cancel` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F3 Cancel"` | Every line is `Pass`. Requests 87 / 0 failed and assertions 82 / 0 failed: 33 requests in the folder (14 `happy path`, 19 `rule violations`), plus 37 setup calls from the pre-request scripts (30 sales and 7 second calls) and 17 read-back GETs from the test scripts. The closing "Postman credentials are required…" line is about cloud publishing only | | |

### B. The F0b, F1, F1b and F2 folders, unchanged and green (same API)

F3 adds the `F3 Cancel` folder and changes no other request or assertion. The expected totals for each folder are the
ones signed off in `docs/qa/build-F1.md`, `docs/qa/build-F1b.md` and `docs/qa/build-F2.md`.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 35 | GET-1 | whole folder `F2 Retrieve` | The step 34 command with `-i "F2 Retrieve"` | Every line is `Pass`. Requests 25 / 0 failed, assertions 26 / 0 failed: the totals F2 signed off | | |
| 36 | SHAPE | whole folder `F1b Layered models` | The step 34 command with `-i "F1b Layered models"` | Every line is `Pass`. Requests 10 / 0 failed, assertions 10 / 0 failed | | |
| 37 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | whole folder `F1 Sell` | The step 34 command with `-i "F1 Sell"` | Every line is `Pass`. Requests 75 / 0 failed, assertions 73 / 0 failed | | |
| 38 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | — (the collection file) | `git diff --numstat main...HEAD -- tests/postman`, then `git diff -U0 main...HEAD -- tests/postman \| Select-String '^(-[^-]\|@@)'` | The first prints `5024 0 tests/postman/Uinsure.Policies.postman_collection.json`: lines added, none removed. The second prints a single hunk header, `@@ -3525,0 +3526,5024 @@`, and no removed line: the F3 folder is appended after the F2 folder, so no F0b, F1, F1b or F2 line changed | | |
| 39 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c, GET-1, SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | the whole collection | The step 34 command without `-i` | Every line is `Pass`. Requests 199 / 0 failed, assertions 193 / 0 failed (`F0b` 2 + `F1 Sell` 75 + `F1b` 10 + `F2 Retrieve` 25 + `F3 Cancel` 87 requests; 2 + 73 + 10 + 26 + 82 assertions). F2 signed off 112 / 111 | | |

### C. Hand requests: the rows Postman doesn't carry (same API; the shell setup and H1–H9 above)

`PAY-` and `REF-` references are the prefix and 32 lower-case hex digits (a GUID in `N` format). A claim reference is
`CLM-{policy reference}-{n}`, where n counts the policy's claims from 1.

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 40 | CANCEL-2, CANCEL-1a, CANCEL-3 | H1 | Run H1 (all three lines) | The sale is `201`, with `payments[0].reference` = `PAY-` + 32 hex digits. The cancel is `201`, no `Location`: `cancellationDate` T, `ruleApplied` CANCEL-1a, `totalRefund` 512.34, `terms` = [{1, CANCEL-1a, 365, 0, 365, 512.34}], `refunds` = [{`reference` `REF-` + 32 hex digits, `type` Card, `amount` 512.34, `paymentReference` = the sale's `PAY-` reference}]. The GET is `200`: `status` Cancelled (it would be Pending), `cancellation` {T, CANCEL-1a, 512.34}, `refunds` = the same single entry, the `REF-` and `PAY-` values unchanged; `payments` still the one Card payment of 512.34 and `terms[0].paymentReference` that `PAY-`; `hasClaims` false, `claims` `[]` | | |
| 41 | CANCEL-3, CANCEL-1b | H2 | Run H2 | The sale is `201`. The cancel, with `Content-Type: application/json` and no body, is `201`: `cancellationDate` T, `ruleApplied` CANCEL-1b, `terms` = [{1, CANCEL-1b, 365, 1, 364, 276.15}], `totalRefund` 276.15, one refund (`REF-…`, DirectDebit, 276.15, the sale's `PAY-…`) | | |
| 42 | CANCEL-4, CANCEL-3 | H3 | Run H3 (all three lines) | The first cancel is `201`: CANCEL-1b, `terms` = [{1, CANCEL-1b, 365, 1, 364, 401.20}], one refund to DirectDebit for 401.20. The second, dated T − 1, is `409`, not `422` CANCEL-3: `type` /problems/cancel-4, `title` "Policy already cancelled", `detail` "The policy was already cancelled on T.", `instance` /policies/QA-F3-TWICE/cancellation, `ruleId` CANCEL-4, `cancellationDate` T, a `traceId`, no `violations` | | |
| 43 | CANCEL-4, COULD-1, CANCEL-3 | H4 | Run H4 | `409`, not `422` CANCEL-3: /problems/cancel-4, `title` "Policy already cancelled", `detail` "The policy was already cancelled on T.", `instance` /policies/QA-F3-TWICE/cancellation-quote, `ruleId` CANCEL-4, `cancellationDate` T, no `violations` | | |
| 44 | COULD-2c, COULD-2a | H5 | Run H5 (both lines) | The claim dated T + 1 is `409`, not `422` COULD-2a: /problems/could-2c, `title` "Claim on a cancelled policy", `detail` "The policy was cancelled on T, so a claim cannot be recorded.", `instance` /policies/QA-F3-TWICE/claims, `ruleId` COULD-2c, `cancellationDate` T, no `violations`. The GET is `200`: `status` Cancelled, `cancellation` {T, CANCEL-1b, 401.20}, the one refund, `hasClaims` false, `claims` `[]` | | |
| 45 | COULD-2 | H6 | Run H6 (all six lines) | The sale is `201`. Each of the four claims is `400` `application/problem+json`, `title` "One or more validation errors occurred.", `instance` /policies/QA-F3-CLAIM/claims, a `traceId`, no `ruleId`, never `500` (A1/I1): `19/10/2026` → `errors` has `$.claimDate`; the unknown field → `errors` has `$.severity`; the malformed JSON → `errors` has `$.claimDate` (the reader's position); `{}` → `errors.claimDate` = ["The ClaimDate field is required."]. The first three also carry `errors.request` = ["The request field is required."], ASP.NET's default when the body can't be read. The GET is `200` with `hasClaims` false and `claims` `[]`: nothing was recorded | | |
| 46 | COULD-2, COULD-1 | H7 | Run H7 (all five lines) | The first claim is `201`, `Location: /policies/QA-F3-CLAIM`, `reference` CLM-QA-F3-CLAIM-1, `policyReference` QA-F3-CLAIM, `claimDate` T, `description` the 500 `x`, `hasClaims` true. The second is `201`, CLM-QA-F3-CLAIM-2, `description` null. The quote for T + 15 is `200`: `quotedOn` T, `ruleApplied` COULD-2 (CANCEL-1c would refund 329.34), `terms` = [{1, COULD-2, 365, 16, 349, 0.00}], `totalRefund` 0.00 (raw text `0.00`), `refunds` `[]`. The cancel for T + 15 is `201` with the same values and no `quotedOn`. The GET is `200`: `status` Cancelled, `hasClaims` true, `claims` = [CLM-QA-F3-CLAIM-1 (T, the 500 `x`), CLM-QA-F3-CLAIM-2 (T, null)] in that order, `cancellation` {T + 15, COULD-2, 0.00}, `refunds` `[]`, `payments` still the one Card payment of 344.44 | | |
| 47 | CANCEL-3 | H8 | Run H8 (all three lines) | The sale is `201`. Both cancels are `400`, `instance` /policies/QA-F3-SHAPE/cancellation, no `ruleId`: `2026/11/01` → `errors` has `$.cancellationDate`; the malformed JSON → `errors` has `$.cancellationDate`. Neither carries an `errors.request` entry, because the cancel body is optional | | |
| 48 | COULD-1 | H9 | Run H9 (all four lines) | `2027-02-30` (not a calendar date) → `400`, `errors.cancellationDate` = ["The cancellation date must be a date in the form yyyy-MM-dd."]; `18/11/2026` → the same `400`; `&reference=OTHER-REF` → `400`, `errors.reference` = ["Unknown query parameter."]. Each has `instance` /policies/QA-F3-SHAPE/cancellation-quote and no `ruleId`. The GET is `200`: `status` Pending, `cancellation` null, `refunds` `[]` (H8 and H9 changed nothing) | | |
| 49 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | — (the API console) | Look at the API console output for sections A to C | No `fail:` or `crit:` line: nothing went down the 500 path (`UnexpectedExceptionHandler`, logged at Error). One `warn: Microsoft.EntityFrameworkCore.Query[20504]` line (more than one collection `Include`, single query) may appear at the first lookup; it's a performance hint, not a fault. No policyholder name, date of birth, address, postcode or claim description appears anywhere in the console | | |

### D. Rows that need a fake clock, a seeded policy or a race: covered by tests

Stop the API first (it locks the build output). These rows can't run live. A 366-day term needs a start between
1 March 2027 and 29 February 2028, which is more than 60 days away (SELL-2). Only a 366-day term can put a refund
exactly on half a penny: with 365 days, amount in pence × unused ÷ 365 can never end in .5, because 365 is odd. The
BST edge, an expired policy and a quote that crosses UK midnight need a `FakeTimeProvider`. The concurrency 409 needs
a second save at an exact point. Each command prints `total: n, failed: 0, succeeded: n, skipped: 0`.

| # | Rule | Covered by test | Command | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 50 | CANCEL-1c, CANCEL-6 | `CancelProRataTests` (Api.Tests): `Cancel_on_start_plus_15_refunds_the_unused_days` (start 2026-11-08, cancel 2026-11-23: 365, 16, 349, 504.07), `Cancel_mid_term_of_a_365_day_term_refunds_the_unused_days_over_365` (730.00, 2027-03-04: 139 used, 226 unused, 452.00), `Cancel_mid_term_of_a_366_day_term_refunds_the_unused_days_over_366` (732.00, term 2027-03-01 to 2028-02-29, 2027-06-01: 93 used, 273 unused, 546.00; a 365-day count would give 545.49). `CancelProRataTests` (Domain.Tests), including `Calculation_on_start_plus_15_of_a_366_day_term_divides_by_366` and `Cancel_on_start_plus_15_of_a_366_day_term_refunds_over_366_days` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelProRataTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.CancelProRataTests"` | 3 passed, then 7 passed. Each Api row reads the policy back whole: `status` Cancelled, the cancellation and the refund stored | | |
| 51 | CANCEL-6, COULD-1 | `Refund_of_exactly_half_a_penny_rounds_away_from_zero` (`CancelRoundingTests`: 100.01 × 183 ÷ 366 = 50.005 exactly → `50.01`), `Quote_of_exactly_half_a_penny_rounds_away_from_zero` (`CancellationQuoteTests`: 120.03 × 61 ÷ 366 = 20.005 exactly → `20.01`). Domain.Tests: `MoneyTests` (half a penny on an even penny, negative half a penny, just under half, more than 2 places) and `CancelRoundingTests` (rounded once) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Refund_of_exactly_half_a_penny_rounds_away_from_zero" --filter-method "*.Quote_of_exactly_half_a_penny_rounds_away_from_zero"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.MoneyTests" --filter-class "Uinsure.Policies.Domain.Tests.CancelRoundingTests"` | 2 passed, then 6 passed. Over HTTP the raw JSON text is `50.01` and `20.01`: banker's rounding would write `50.00` and `20.00`, and no rounding would write `50.005` and `20.005` | | |
| 52 | CANCEL-3, CANCEL-7, COULD-2a | `CancelDateTests` (Api.Tests, 7): the no-body, `{}` and null rows on a UK today of their own (262.69, 373.09, 250.50), yesterday, the BST pair (2027-06-30T23:30Z: a date of 2027-06-30 is `422` past with `today` 2027-07-01; 2027-07-01 is accepted, 366 days, 272.90), and `Past_date_after_the_end_of_cover_reports_CANCEL_3_and_CANCEL_7` (`violations` = [CANCEL-3, CANCEL-7]). Services.Tests: `PolicyServiceCancelDateTests` (8) and `PolicyServiceClaimDateTests` (5): BST and GMT at 23:30 UTC, and each service reads the clock once | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelDateTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceCancelDateTests" --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceClaimDateTests"` | 7 passed, then 13 passed. Today is always the UK date, never the UTC date | | |
| 53 | COULD-1, CANCEL-3 | `Quote_read_across_UK_midnight_is_checked_and_quoted_on_one_UK_today` (Api.Tests: the clock moves an hour on every read from 2026-10-17T22:30Z; a quote for 2026-10-17 is `200`, `quotedOn` 2026-10-17, CANCEL-1b, 365, 1, 364, 452.76, not a `422` from a second read on 2026-10-18; finding A2/R1). Services.Tests: `Quote_reads_the_clock_once`, `Cancel_reads_the_clock_once`, `RecordClaim_reads_the_clock_once` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Quote_read_across_UK_midnight_is_checked_and_quoted_on_one_UK_today"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-method "*.Quote_reads_the_clock_once" --filter-method "*.Cancel_reads_the_clock_once" --filter-method "*.RecordClaim_reads_the_clock_once"` | 1 passed, then 3 passed. One request has one UK today: the date CANCEL-3 checks is the `quotedOn` it answers with | | |
| 54 | COULD-1, CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-7 | `CancellationQuoteTests` (Api.Tests, 16: agree with a later cancel (315.29), byte-for-byte unchanged read-back, 1a/1b/1c boundaries, half-penny, past, midnight, after the end, after a claim, the five 400 rows and the 404). Domain.Tests `CancellationQuoteTests` (7). Services.Tests `PolicyServiceQuoteTests` (5: computes without saving, BST rows, the same calculation as the cancel, clock once) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancellationQuoteTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.CancellationQuoteTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceQuoteTests"` | 16, then 7, then 5 passed. The quote never saves (finding A2/R2: the domain owns the quote decision) | | |
| 55 | CONCURRENCY | `CancelClaimConcurrencyTests` (Api.Tests, untraited): `Cancellation_that_loses_a_race_answers_409_concurrency_and_stores_nothing`, `Claim_that_loses_a_race_answers_409_concurrency_and_stores_nothing`, `Cancellation_retried_after_a_lost_race_is_accepted` (269.98) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelClaimConcurrencyTests"` | 3 passed. A write that loses the race is `409` `type` /problems/concurrency, `title` "The policy was changed by another request", `detail` "Retry the request.", no `ruleId`; the policy reads back Active as sold, and a retry is accepted | | |
| 56 | COULD-2 | `ClaimResponseTests` (Api.Tests): `Claim_answers_with_its_own_claim_when_another_claim_lands_straight_after_it`, `Claim_answers_with_its_own_claim_when_the_policy_cannot_be_read_again_after_the_save` (findings A2/R3 and A2/S1) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.ClaimResponseTests"` | 2 passed. The 201 is always this request's own claim, never another request's and never a 500 | | |
| 57 | COULD-2 | `Contract_records_carry_no_validation_attribute_on_a_primary_constructor_property` (`ContractRecordTests`, Architecture.Tests; finding A1/I2), with `Claim_without_a_claimDate_is_rejected_with_400` and `Claim_description_of_501_characters_is_rejected_with_400` (`ClaimRecordTests`; A1/I1) | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.ContractRecordTests"`, then `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Claim_without_a_claimDate_is_rejected_with_400" --filter-method "*.Claim_description_of_501_characters_is_rejected_with_400"` | 1 passed, then 2 passed. No `Api.Contracts` record has a DataAnnotations attribute on a property backed by a primary-constructor parameter, the cause of the stub-gate 500 | | |
| 58 | CANCEL-7, COULD-2a, COULD-2b | `CancelAfterCoverTests` (3: the last day of cover, 0.00 and no Refund; the day after, `422`; an expired policy cancelled today, `422` CANCEL-7 alone), `ClaimCoverTests` (4: the day before the start, before a future start, the last day of cover accepted after expiry, the day after the end), `ClaimDateTests` (3: tomorrow, the BST UK date accepted, COULD-2a with COULD-2b) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelAfterCoverTests" --filter-class "Uinsure.Policies.Api.Tests.ClaimCoverTests" --filter-class "Uinsure.Policies.Api.Tests.ClaimDateTests"` | 10 passed | | |
| 59 | COULD-2 | `CancelWithClaimTests` (3: a claim overrides CANCEL-1b, CANCEL-1c (would be 447.82) and CANCEL-1a on a seeded policy stored with `hasClaims` true; each 0.00, `refunds` `[]`), `ClaimRecordTests` (10: recorded on the start date, no description, a second claim listed after the first, 500 characters accepted, the five 400 rows, the 404) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelWithClaimTests" --filter-class "Uinsure.Policies.Api.Tests.ClaimRecordTests"` | 13 passed | | |
| 60 | CANCEL-4, COULD-2c | `CancelTwiceTests` (3, including `Cancelling_twice_with_a_past_date_answers_409_before_the_date_rule`), `ClaimOnCancelledTests` (2, including `Claim_with_a_future_date_on_a_cancelled_policy_answers_409_before_the_date_rules`). Each reads the first cancellation back exactly as stored | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelTwiceTests" --filter-class "Uinsure.Policies.Api.Tests.ClaimOnCancelledTests"` | 5 passed | | |
| 61 | CANCEL-2, CANCEL-1a, CANCEL-1b | `CancelRefundMethodTests` (a seeded payment `PAY-CAN2-SEEDED-1` by DirectDebit; the refund of 286.25 names it), `CancelBeforeStartTests` (2), `CancelCoolingOffTests` (2) in Api.Tests. Services.Tests: `ReferenceGeneratorTests` (4: `PAY-` and `REF-` prefixes, each unique) and `Each_refund_takes_its_reference_from_the_reference_generator` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelRefundMethodTests" --filter-class "Uinsure.Policies.Api.Tests.CancelBeforeStartTests" --filter-class "Uinsure.Policies.Api.Tests.CancelCoolingOffTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.ReferenceGeneratorTests" --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceCancelRefundTests"` | 5 passed, then 5 passed | | |
| 62 | CANCEL-3 | `CancelShapeTests` (Api.Tests: unknown field, a date not in yyyy-MM-dd, malformed JSON, the 404) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelShapeTests"` | 4 passed | | |
| 63 | GET-1 | `CancelledStatusTests` (Domain.Tests): `Status_of_a_policy_cancelled_before_its_start_is_Cancelled`, `Status_of_a_policy_cancelled_from_a_future_date_is_Cancelled_while_in_cover` | `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.CancelledStatusTests"` | 2 passed. Cancelled overrides Pending and Active, whatever the cancellation date | | |
| 64 | CANCEL-1a | every test traited CANCEL-1a | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=CANCEL-1a"` | 5 passed: Api.Tests 3, Domain.Tests 2 | | |
| 65 | CANCEL-1b | every test traited CANCEL-1b | The step 64 command with `Rule=CANCEL-1b` | 7 passed: Api.Tests 3, Domain.Tests 4 | | |
| 66 | CANCEL-1c | every test traited CANCEL-1c | The step 64 command with `Rule=CANCEL-1c` | 17 passed: Api.Tests 8, Domain.Tests 9 | | |
| 67 | CANCEL-2 | every test traited CANCEL-2 | The step 64 command with `Rule=CANCEL-2` | 15 passed: Api.Tests 4, Domain.Tests 8 (one theory over Card, DirectDebit and Cheque), Services.Tests 3 | | |
| 68 | CANCEL-3 | every test traited CANCEL-3 | The step 64 command with `Rule=CANCEL-3` | 32 passed: Api.Tests 15, Domain.Tests 7, Services.Tests 10 | | |
| 69 | CANCEL-4 | every test traited CANCEL-4 | The step 64 command with `Rule=CANCEL-4` | 6 passed: Api.Tests 3, Domain.Tests 3 | | |
| 70 | CANCEL-6 | every test traited CANCEL-6 | The step 64 command with `Rule=CANCEL-6` | 12 passed: Api.Tests 4, Domain.Tests 8 | | |
| 71 | CANCEL-7 | every test traited CANCEL-7 | The step 64 command with `Rule=CANCEL-7` | 12 passed: Api.Tests 5, Domain.Tests 7 | | |
| 72 | COULD-1 | every test traited COULD-1 | The step 64 command with `Rule=COULD-1` | 28 passed: Api.Tests 16, Domain.Tests 7, Services.Tests 5 | | |
| 73 | COULD-2 | every test traited COULD-2 | The step 64 command with `Rule=COULD-2` | 26 passed: Api.Tests 15, Domain.Tests 10, Architecture.Tests 1 | | |
| 74 | COULD-2a | every test traited COULD-2a | The step 64 command with `Rule=COULD-2a` | 12 passed: Api.Tests 4, Domain.Tests 3, Services.Tests 5 | | |
| 75 | COULD-2b | every test traited COULD-2b | The step 64 command with `Rule=COULD-2b` | 13 passed: Api.Tests 6, Domain.Tests 7 | | |
| 76 | COULD-2c | every test traited COULD-2c | The step 64 command with `Rule=COULD-2c` | 4 passed: Api.Tests 2, Domain.Tests 2 | | |
| 77 | GET-1 | every test traited GET-1 | The step 64 command with `Rule=GET-1` | 64 passed. F2 signed off 59; F3 adds the three 404 rows of E3, E4 and E5 and the two `CancelledStatusTests` | | |
| 78 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | `RuleCoverageTests` | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`, then open `tests/rule-coverage.pending.txt` | 11 passed. The pending file has 9 IDs (CANCEL-5, RENEW-1 to RENEW-6 with 3a and 3b, COULD-3) and none of the 13 F3 IDs (F2 signed off 22: F3 took 13 out) | | |
| 79 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | the whole suite | `dotnet test --solution Uinsure.Policies.slnx` | 528 passed, 0 failed, 0 skipped (F2 signed off 374) | | |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- How these expectations were checked. On 2026-10-19, between about 02:40 and 02:55 UTC (03:40 to 03:55 UK time, BST;
  UK today 2026-10-19), the tester ran these at HEAD 520626a, whose `src/` and `tests/` match a59fb6c: the full suite
  (528 / 0 / 0); every command in section D, with the counts given; each per-rule trait filter, with the per-project
  split read from the trait attributes (it matches every total); and the API from the worktree on 5080
  (`Hosting environment: Development`, `/health/live` and `/health/ready` both `200 Healthy`). With the API up, the
  Postman CLI 1.75.0 ran `F3 Cancel` (87 / 0 requests, 82 / 0 assertions), `F1 Sell` (75 / 0, 73 / 0),
  `F1b Layered models` (10 / 0, 10 / 0), `F2 Retrieve` (25 / 0, 26 / 0) and the whole collection (199 / 0, 193 / 0).
  Step 38's diff was also run. The hand requests were sent with Git Bash `curl` under `QAV-F3-*` references, not this
  script's `QA-F3-*`, so yours stay free. Their statuses and bodies are the ones section C expects. The console had
  0 `fail:` and 0 `crit:` lines, one `warn:` (EF Core 20504), and no name, address, postcode or description. The
  harness doesn't let the tester run `powershell.exe`, so the shell setup and the PowerShell lines of H1–H9 haven't
  been run as written. If one misbehaves, note it here: that is a script defect, not an API defect. The Actual and
  Pass/Fail columns are yours.
- Step 8 (Postman CANCEL-6) doesn't by itself tell half-away-from-zero from banker's rounding. 657.3152… rounds to
  657.32 either way. What it does show live is a 2 dp refund that is rounded, not truncated. The exact half-penny can
  only be shown on a 366-day term (section D's reasoning), so step 51 is the CANCEL-6 proof.
- For your ruling, not raised as a finding: the 409 `detail` texts differ from the contract's examples. The API says
  "The policy was already cancelled on 2026-10-19." (CANCEL-4) and "The policy was cancelled on 2026-10-19, so a claim
  cannot be recorded." (COULD-2c). The contract's examples name the reference and say "with effect from". The tests
  assert only that the detail names the original date, and the `cancellationDate` extension carries it. The contract
  marks these texts as examples.
- For your ruling: a body that is only a line break (what `'' | curl.exe … --data-binary "@-"` sends from PowerShell) is
  a `400` with `errors["$"]` ("The input does not contain any JSON tokens…"). It is not read as an empty body. The
  contract's empty forms (no body, `{}`, a null date) are all `201` (steps 1, 6, 7 and 41). Whitespace is not one of
  them, so this looks right, but no test pins it.
- For your ruling, carried over from F1: the System.Text.Json 400 messages name the internal contract type, for example
  "…could not be mapped to any .NET member contained in type 'Uinsure.Policies.Api.Contracts.RecordClaimRequest'". Sell
  has said the same about `SellPolicyRequest` since F1. They also add `errors.request` = ["The request field is
  required."] next to the JSON error. Both are ASP.NET defaults; neither leaks data.
- Open P2s from the last review, both non-blocking and both PASS verdicts: A2.3/R1 (the header comment of
  `tests/Uinsure.Policies.Services.Tests/PolicyServiceQuoteTests.cs`, lines 12–13, still names the old `Quote` methods)
  and A2.3/S1 (no cap on the number of claims per policy; growth is bounded only by the write rate limit).
- Run Postman and the hand steps away from UK midnight. Both compute UK today themselves, as the API does, so a run that
  crosses midnight can sell on one date and cancel or claim on the next. The clocks go back on 2026-10-25. The
  PowerShell setup uses the Europe/London zone (`GMT Standard Time`), so it follows the change.

## Sign-off

| | |
|---|---|
| Tester | |
| Date | |
| Commit signed off | |
| `dotnet test` result | |
| Postman CLI result | |
| Steps passed | |
| Defects raised (finding ids) | |
| Verdict | ☐ Reject (findings above)  ☐ Accept F3 |
| Signature | |
