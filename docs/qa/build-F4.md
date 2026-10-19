# QA script — build / F4 — Renew, with the cheque rule and CANCEL-5

Rule IDs: RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5
Branch: feature/build/F4   Worktree: .claude/worktrees/build-F4
Commit under test: b5ca22e (the last code or test commit: d931d20 and b5ca22e closed QA step 34 after ed44a60, and the A1.3 reviewer verdict is PASS at b5ca22e. This script's own commits change only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080   Environment: local (`tests/postman/local.postman_environment.json`)

F4 adds one route. `POST /policies/{reference}/renewals` (E6) takes `{ "amount", "paymentMethod"? }`. It answers 201 with
`Location: /policies/{reference}` and the `PolicyResponse`, with the next term appended under the same reference. The
new term starts the day after the latest term ends (RENEW-4). A renewal is allowed from the latest term's end − 30 days
to that end, inclusive, on UK today (RENEW-1/2). An auto-renewing policy's renewal is paid by the request's method, or
by the current term's method when none is sent (RENEW-3a), but never by Cheque, whether sent or defaulted (COULD-3). A
policy that does not auto-renew is renewed unpaid, and sending a `paymentMethod` for it is a 422 (RENEW-3b). The amount
must be above 0 (RENEW-5). A cancelled policy can't be renewed: 409, returned alone (RENEW-6). Cancelling a renewed
policy refunds the current term by the normal rules and a future term in full (CANCEL-5).

**What can and can't run live.** Postman and the hand steps can't fake time, and SELL-2 bars a start more than 60 days
ahead. So no policy sold today can reach its renewal window, which opens about 11 months after the start. Every live
renewal therefore breaks RENEW-1/2. The live rows show RENEW-1/2 itself, the 409 and 404 rows, the 400 shapes, and
RENEW-3b, RENEW-5 and COULD-3 listed **alongside** RENEW-1/2 in `violations[]`. Every success row (RENEW-3a, RENEW-3b's
unpaid term, RENEW-4, COULD-3 on its own) and every CANCEL-5 row needs a fake clock. Those are in section D, as
"covered by test", for you to run with `dotnet test`. The `F4 Renew` folder's `happy path` sub-folder is empty for
this reason.

The Rule column holds a bare rule id. A row tagged with several ids lists them comma-separated. `GET-1` marks the 404
row of the new route. `CONCURRENCY` marks the untraited concurrency row, as in F3. The 400 shape rows carry the rule of
the input they guard, as the Postman names and the test traits do: `RENEW-5` for the amount, `RENEW-3a` for the payment
method, `RENEW-4` for an unknown member or a body that can't be read. Reviewer findings in
`docs/trace/build/F4/verdicts/` are cited as `A1/R1` to `A1/R3` (all resolved at ed44a60) and `A1.2/R1`.

Commands are for PowerShell, run from the worktree root. Inside the tables, `\|` is a Markdown escape: type a
plain `|` (the PowerShell pipe).

**Dates and amounts.** In the expected values, `T` is UK today (Europe/London). `E` is `T` + 1 year − 1 day, the end of
a policy that starts on `T`, and `W` is `E` − 30 days, the first day of its renewal window. For a policy that starts on
`T` + n, `E+n` is its end (`T` + n + 1 year − 1 day) and `W+n` is `E+n` − 30. Postman and the hand steps compute these
from the real clock, as the API does. Unless a row says otherwise, each sale is the F4 default: Household, Katherine
Johnson (born 1981-08-26), 48 Quay St, M3 4LZ (Postman adds Unit 5).

## Preconditions

- [ ] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: __ / __ / __). Expected: 637 / 0 / 0.
- [ ] Ports 5080 and 7080 are free (`netstat -ano | findstr ":5080 :7080"` prints no LISTENING line). A running API also locks the build output, so stop it before any `dotnet build` or `dotnet test`.
- [ ] The fixed `QA-F4-*` references of the hand steps are free. Either start from an empty database (with the API stopped: `Remove-Item src/Uinsure.Policies.Api/policies.development.db*`; start-up recreates it with `EnsureCreated`), or, once the API is up, check that a GET of each of `QA-F4-WIN`, `QA-F4-CHQ`, `QA-F4-NOAR`, `QA-F4-SHAPE` and `QA-F4-NONE` gives `404`.
- [ ] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).
- [ ] You are running well away from UK midnight. A sale and its renewal or cancel must fall on the same UK date (see the notes).

### Starting the API

`dotnet run --project src/Uinsure.Policies.Api`. The console shows `Now listening on: https://localhost:7080`,
`Now listening on: http://localhost:5080` and `Hosting environment: Development`. Keep the console in view: step 38
reads it.

### Shell setup for the hand steps (H1–H8)

Paste this once into the PowerShell window you use for the hand steps. UK today comes from Europe/London, as in the
API. `Day n` is UK today + n days, as `yyyy-MM-dd`. `Sell` now takes the auto-renew flag as its last argument.

```powershell
$ukToday = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'GMT Standard Time').Date
function Day([int]$offset) { $ukToday.AddDays($offset).ToString('yyyy-MM-dd') }
function Sell([string]$reference, [string]$start, [string]$amount, [string]$method, [string]$autoRenew) {
  ('{"reference":"' + $reference + '","type":"Household","startDate":"' + $start + '","amount":' + $amount + ',"autoRenew":' + $autoRenew + ',"paymentMethod":"' + $method + '","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}') |
    curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-"
}
function Renew([string]$reference, [string]$json) {
  $json | curl.exe -s -i -X POST ("http://localhost:5080/policies/" + $reference + "/renewals") -H "Content-Type: application/json" --data-binary "@-"
}
function PostTo([string]$path, [string]$json) {
  $json | curl.exe -s -i -X POST ("http://localhost:5080/policies/" + $path) -H "Content-Type: application/json" --data-binary "@-"
}
function GetFrom([string]$path) { curl.exe -s -i ("http://localhost:5080/policies/" + $path) }
```

The JSON is piped to curl so that PowerShell does not strip its quotes. Don't pipe an empty string to send "no body":
PowerShell adds a line break, and a body that is only a line break is not an empty body. H7 sends no body by leaving
out `--data-binary`.

### Hand requests

```powershell
# H1 [RENEW-1/2] the whole 422 on a policy sold today: the window is W to E
Sell 'QA-F4-WIN' (Day 0) '377.42' 'Card' 'true'
Renew 'QA-F4-WIN' '{"amount":395.10}'
GetFrom 'QA-F4-WIN'

# H2 [RENEW-1/2] the lower-case route reference finds the same policy
Renew 'qa-f4-win' '{"amount":395.10}'

# H3 [RENEW-5] three 422 rules in one request, in rule-ID order
Renew 'QA-F4-WIN' '{"amount":-10.00,"paymentMethod":"Cheque"}'

# H4 [COULD-3] a Cheque policy that auto-renews: defaulted Cheque, sent Cheque, then Card and DirectDebit
Sell 'QA-F4-CHQ' (Day 0) '366.81' 'Cheque' 'true'
Renew 'QA-F4-CHQ' '{"amount":410.55}'
Renew 'QA-F4-CHQ' '{"amount":410.55,"paymentMethod":"Cheque"}'
Renew 'QA-F4-CHQ' '{"amount":410.55,"paymentMethod":"Card"}'
Renew 'QA-F4-CHQ' '{"amount":410.55,"paymentMethod":"DirectDebit"}'
GetFrom 'QA-F4-CHQ'

# H5 [RENEW-3b] a Cheque policy that does not auto-renew: a Cheque sent, a null method, no method
Sell 'QA-F4-NOAR' (Day 0) '358.64' 'Cheque' 'false'
Renew 'QA-F4-NOAR' '{"amount":372.90,"paymentMethod":"Cheque"}'
Renew 'QA-F4-NOAR' '{"amount":372.90,"paymentMethod":null}'
Renew 'QA-F4-NOAR' '{"amount":372.90}'

# H6 [RENEW-6] cancel it from Day 30, then renew breaking RENEW-1/2, RENEW-3b and RENEW-5; then a shape error
PostTo 'QA-F4-NOAR/cancellation' ('{"cancellationDate":"' + (Day 30) + '"}')
Renew 'QA-F4-NOAR' '{"amount":0,"paymentMethod":"Card"}'
Renew 'QA-F4-NOAR' ('{"amount":0,"paymentMethod":"Card","startDate":"' + (Day 365) + '"}')
GetFrom 'QA-F4-NOAR'

# H7 [RENEW-5] the body shapes Postman doesn't carry, then the amount edges beside RENEW-1/2
Sell 'QA-F4-SHAPE' (Day 22) '344.27' 'DirectDebit' 'true'
Renew 'QA-F4-SHAPE' '{"amount":null}'
Renew 'QA-F4-SHAPE' '{"amount":"401.16"}'
curl.exe -s -i -X POST http://localhost:5080/policies/QA-F4-SHAPE/renewals -H "Content-Type: application/json"
curl.exe -s -i -X POST http://localhost:5080/policies/QA-F4-SHAPE/renewals
Renew 'QA-F4-SHAPE' '{"amount":0.01}'
Renew 'QA-F4-SHAPE' '{"amount":1000000.00}'
Renew 'QA-F4-SHAPE' '{"amount":0.00}'
GetFrom 'QA-F4-SHAPE'

# H8 [GET-1] the 400 shape comes before the 404; an unknown query parameter
Renew 'QA-F4-NONE' '{"amount":1.001}'
Renew 'QA-F4-NONE' '{"amount":401.16}'
'{"amount":401.16}' | curl.exe -s -i -X POST "http://localhost:5080/policies/QA-F4-SHAPE/renewals?amount=5" -H "Content-Type: application/json" --data-binary "@-"
```

## Steps

### A. The F4 Renew folder (API running in Development, from "Starting the API")

Send each request from the folder `F4 Renew` in the Postman app, or run it alone with
`postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "<request name>" --verbose`.
Every request is in `rule violations`. Each makes its own `F4-…` reference. Its pre-request script sells the policy
(and, where stated, then cancels it), and its first `pm.test` checks that every setup call answered as listed. "A rule
problem" means `application/problem+json` with `type`, `title`, `status`, `instance` /policies/{reference}/renewals,
`ruleId` and a non-empty `traceId`. For a 422, `violations` lists every rule broken, in rule-ID order, each with a
non-empty `detail`, and with two or more the top-level `detail` is "n rules failed.". "Read-back unchanged" means the
GET is `200` with exactly one term (the sold start and end), one payment and `cancellation` null: a rejected renewal
adds nothing.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | RENEW-1/2 | RENEW-1/2 · renew on the day of sale → 422 | Send. Setup: sell start T, 384.29 by Card, auto-renew on. Body `{"amount": 401.16}` | `422` rule problem: `type` /problems/renew-1-2, `title` "Outside the renewal window", `ruleId` RENEW-1/2, `violations` = [RENEW-1/2], `windowStart` W, `windowEnd` E. Read-back unchanged. 3 `pm.test` | | |
| 2 | RENEW-1/2 | RENEW-1/2 · renew a Pending policy starting today+30 → 422 | Send. Setup: sell start T + 30, 412.34 by DirectDebit, auto-renew on. Body `{"amount": 427.18}` | As step 1, with `windowStart` W+30 and `windowEnd` E+30: the window is measured from the latest term's end, not from today. Read-back unchanged. 3 `pm.test` | | |
| 3 | RENEW-1/2 | RENEW-1/2 · auto-renew off, Card and amount 0, today → 422 | Send. Setup: sell start T, 333.27 by Card, auto-renew off. Body `{"amount": 0, "paymentMethod": "Card"}` | `422` /problems/renew-1-2, `ruleId` RENEW-1/2, `detail` "3 rules failed.", `violations` = [RENEW-1/2, RENEW-3b, RENEW-5] in that order: none returns early. Read-back unchanged. 3 `pm.test` | | |
| 4 | RENEW-3b | RENEW-3b · auto-renew off with a paymentMethod, today → 422 | Send. Setup: sell start T, 341.58 by Card, auto-renew off. Body `{"amount": 362.40, "paymentMethod": "Card"}` | `422` /problems/renew-1-2 (the first rule names the problem), `detail` "2 rules failed.", `violations` = [RENEW-1/2, RENEW-3b]. Read-back unchanged. 3 `pm.test` | | |
| 5 | RENEW-5 | RENEW-5 · amount 0, today → 422 | Send. Setup: sell start T, 402.17 by DirectDebit, auto-renew on. Body `{"amount": 0}` | `422` /problems/renew-1-2, `detail` "2 rules failed.", `violations` = [RENEW-1/2, RENEW-5]. Read-back unchanged. 3 `pm.test` | | |
| 6 | RENEW-5 | RENEW-5 · amount negative, today → 422 | Send. Setup: sell start T, 421.09 by DirectDebit, auto-renew on. Body `{"amount": -25.50}` | As step 5: `violations` = [RENEW-1/2, RENEW-5]. Read-back unchanged. 3 `pm.test` | | |
| 7 | COULD-3 | COULD-3 · explicit Cheque on an auto-renewing policy, today → 422 | Send. Setup: sell start T, 459.63 by DirectDebit, auto-renew on. Body `{"amount": 472.30, "paymentMethod": "Cheque"}` | `422` /problems/renew-1-2, `detail` "2 rules failed.", `violations` = [RENEW-1/2, COULD-3]. Read-back unchanged. 3 `pm.test` | | |
| 8 | COULD-3 | COULD-3 · Cheque defaulted from the current term, today → 422 | Send. Setup: sell start T, 501.19 by Cheque, auto-renew on. Body `{"amount": 523.48}` (no method: RENEW-3a defaults it to Cheque) | `422` /problems/renew-1-2, `violations` = [RENEW-1/2, COULD-3], and the COULD-3 `detail` names both Card and DirectDebit: it asks for another method. Read-back unchanged. 3 `pm.test` | | |
| 9 | RENEW-6 | RENEW-6 · renew a policy cancelled today → 409 | Send. Setup: sell start T, 352.48 by Cheque, auto-renew on; cancel with `{}` (`201`). Body `{"amount": 401.75}` | `409` rule problem: `type` /problems/renew-6, `title` "Cancelled policy cannot be renewed", `ruleId` RENEW-6, `cancellationDate` T, a `detail` naming T, no `violations`. Read-back `200`: `status` Cancelled, `cancellation` not null, one term, one payment. 3 `pm.test` | | |
| 10 | RENEW-6 | RENEW-6 · cancelled from today+30, amount 0 by Cheque → 409 alone | Send. Setup: sell start T, 388.21 by Card, auto-renew on; cancel on T + 30 (`201`). Body `{"amount": 0, "paymentMethod": "Cheque"}` | `409` /problems/renew-6 alone, not `422`: the body also breaks RENEW-1/2, RENEW-5 and COULD-3. `cancellationDate` T + 30, a `detail` naming T + 30, no `violations`. Read-back: Cancelled, one term, one payment. 3 `pm.test` | | |
| 11 | GET-1 | GET-1 · renew an unknown policy → 404 | Send (nothing sold). Body `{"amount": 401.16}` | `404` rule problem: `type` /problems/get-1, `title` "Policy not found", `ruleId` GET-1, `reference` {reference}, `detail` "No policy with reference '{reference}'.", no `violations`. 2 `pm.test` | | |
| 12 | RENEW-5 | RENEW-5 · amount missing → 400 | Send. Setup: sell start T, 415.80 by Card, auto-renew on. Body `{}` | `400` `application/problem+json`, `title` "One or more validation errors occurred.", `instance` /policies/{reference}/renewals, a `traceId`, no `ruleId`, `errors` has `amount`. Read-back unchanged. 3 `pm.test` | | |
| 13 | RENEW-5 | RENEW-5 · amount with 3 decimal places → 400 | Send. Same setup. Body `{"amount": 432.105}` | As step 12: `errors` has `amount`. Read-back unchanged. 3 `pm.test` | | |
| 14 | RENEW-5 | RENEW-5 · amount 1,000,000.01 → 400 | Send. Same setup. Body `{"amount": 1000000.01}` | As step 12: `errors` has `amount` (the H6 cap, as on Sell). Read-back unchanged. 3 `pm.test` | | |
| 15 | RENEW-3a | RENEW-3a · unknown payment method → 400 | Send. Same setup. Body `{"amount": 603.27, "paymentMethod": "PayPal"}` | As step 12, but `errors` has `$.paymentMethod`. Read-back unchanged. 3 `pm.test` | | |
| 16 | RENEW-3a | RENEW-3a · integer payment method → 400 | Send. Same setup. Body `{"amount": 603.27, "paymentMethod": 1}` | As step 15: `errors` has `$.paymentMethod` (integer enums are refused). Read-back unchanged. 3 `pm.test` | | |
| 17 | RENEW-4 | RENEW-4 · startDate sent → 400 | Send. Same setup. Body `{"amount": 603.27, "startDate": T}` | As step 12, but `errors` has `$.startDate`: a renewal can't choose its own start (finding A1/R3 renamed the matching test). Read-back unchanged. 3 `pm.test` | | |
| 18 | RENEW-4 | RENEW-4 · malformed JSON → 400 | Send. Same setup. Body `{"amount": 603.27` (no closing brace) | As step 12, with a non-empty `errors`. Read-back unchanged. 3 `pm.test` | | |
| 19 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3 | whole folder `F4 Renew` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F4 Renew"` | Every line is `Pass`. Requests 54 / 0 failed and assertions 53 / 0 failed: 18 requests in the folder (0 `happy path`, 18 `rule violations`), plus 19 setup calls from the pre-request scripts (17 sales and 2 cancels) and 17 read-back GETs from the test scripts. 53 = 17 × 3 + 2 (step 11 has no read-back). The closing "Postman credentials are required…" line is about cloud publishing only | | |

### B. The earlier folders, unchanged and green (same API)

F4 adds the `F4 Renew` folder and changes no other request or assertion. The expected totals for each folder are the
ones signed off in `docs/qa/build-F1.md`, `docs/qa/build-F1b.md`, `docs/qa/build-F2.md` and `docs/qa/build-F3.md`.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 20 | CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c | whole folder `F3 Cancel` | The step 19 command with `-i "F3 Cancel"` | Every line is `Pass`. Requests 87 / 0 failed, assertions 82 / 0 failed: the totals F3 signed off | | |
| 21 | GET-1 | whole folder `F2 Retrieve` | The step 19 command with `-i "F2 Retrieve"` | Every line is `Pass`. Requests 25 / 0 failed, assertions 26 / 0 failed | | |
| 22 | SHAPE | whole folder `F1b Layered models` | The step 19 command with `-i "F1b Layered models"` | Every line is `Pass`. Requests 10 / 0 failed, assertions 10 / 0 failed | | |
| 23 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | whole folder `F1 Sell` | The step 19 command with `-i "F1 Sell"` | Every line is `Pass`. Requests 75 / 0 failed, assertions 73 / 0 failed | | |
| 24 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3 | — (the collection file) | `git diff --numstat main...HEAD -- tests/postman`, then `git diff -U0 main...HEAD -- tests/postman \| Select-String '^(-[^-]\|@@)'` | The first prints `3040 0 tests/postman/Uinsure.Policies.postman_collection.json`: lines added, none removed. The second prints a single hunk header, `@@ -8549,0 +8550,3040 @@`, and no removed line: the F4 folder is appended after the F3 folder, so no earlier line changed | | |
| 25 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, GET-1 | the whole collection | The step 19 command without `-i` | Every line is `Pass`. Requests 253 / 0 failed, assertions 246 / 0 failed (`F0b` 2 + `F1 Sell` 75 + `F1b` 10 + `F2 Retrieve` 25 + `F3 Cancel` 87 + `F4 Renew` 54 requests; 2 + 73 + 10 + 26 + 82 + 53 assertions). F3 signed off 199 / 193 | | |

### C. Hand requests: the rows Postman doesn't carry (same API; the shell setup and H1–H8 above)

The single-violation 422s carry the rule's own extensions at the top level (RENEW-1/2: `windowStart`, `windowEnd`).
With two or more violations, the top level carries no rule extension, and each `violations[]` entry has exactly
`ruleId` and `detail` (see the notes).

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 26 | RENEW-1/2 | H1 | Run H1 (all three lines) | The sale is `201`. The renewal is `422` `application/problem+json`: `type` /problems/renew-1-2, `title` "Outside the renewal window", `status` 422, `detail` "A policy can be renewed from W to E.", `instance` /policies/QA-F4-WIN/renewals, a `traceId`, `ruleId` RENEW-1/2, `windowStart` W, `windowEnd` E, `violations` = [{`ruleId` RENEW-1/2, `detail` the same text}]. The GET is `200`: `status` Active, `currentTermNumber` 1, one term (T to E, 377.42, its `paymentReference` the one Card payment of 377.42), `refunds` `[]`, `cancellation` null | | |
| 27 | RENEW-1/2 | H2 | Run H2 | `422` with the same `windowStart` W and `windowEnd` E as step 26, so the lower-case reference found QA-F4-WIN. `instance` is the path as sent, /policies/qa-f4-win/renewals | | |
| 28 | RENEW-5 | H3 | Run H3 | `422` /problems/renew-1-2, `ruleId` RENEW-1/2, `detail` "3 rules failed.", `violations` = [{RENEW-1/2, "A policy can be renewed from W to E."}, {RENEW-5, "The renewal amount must be greater than 0."}, {COULD-3, "An auto-renewing policy cannot be renewed by Cheque. Use Card or DirectDebit."}] in that order. No top-level `windowStart`, `windowEnd`, `amount` or `paymentMethod` | | |
| 29 | COULD-3 | H4 | Run H4, lines 1–3 (the sale, the defaulted Cheque, the sent Cheque) | The sale is `201`, paid by Cheque. Both renewals are `422` /problems/renew-1-2, `detail` "2 rules failed.", `violations` = [{RENEW-1/2, the window text for W to E}, {COULD-3, "An auto-renewing policy cannot be renewed by Cheque. Use Card or DirectDebit."}]. With no method sent, the default is the current term's Cheque (RENEW-3a), and COULD-3 rejects it as it rejects a Cheque sent | | |
| 30 | RENEW-3a | H4 | Run H4, lines 4–6 (Card, DirectDebit, the GET) | Both renewals are `422` with RENEW-1/2 alone: `detail` the window text, `windowStart` W, `windowEnd` E, `violations` = [RENEW-1/2]. Sending another method clears COULD-3 on a Cheque policy. The GET is `200`: one term (366.81), one Cheque payment of 366.81, nothing added | | |
| 31 | RENEW-3b | H5 | Run H5 (all four lines) | The sale is `201` with `autoRenew` false. The Cheque renewal is `422` /problems/renew-1-2, `detail` "2 rules failed.", `violations` = [{RENEW-1/2, the window text}, {RENEW-3b, "The policy does not auto-renew, so the renewal is unpaid and takes no payment method."}]: no COULD-3, because the policy does not auto-renew. No `paymentMethod` member appears anywhere in the body (finding A1/R2). With `"paymentMethod": null` and with no method, the renewal is `422` with RENEW-1/2 alone (`windowStart` W, `windowEnd` E): a null method is no method | | |
| 32 | RENEW-6 | H6 | Run H6 (all four lines) | The cancel is `201` (CANCEL-1c: 358.64 × 334 ÷ 365 = 328.1801…, 328.18 to Cheque). The renewal is `409`, not `422`: `type` /problems/renew-6, `title` "Cancelled policy cannot be renewed", `detail` "The policy was cancelled on T + 30, so it cannot be renewed.", `instance` /policies/QA-F4-NOAR/renewals, `ruleId` RENEW-6, `cancellationDate` T + 30, a `traceId`, no `violations` (RENEW-1/2, RENEW-3b and RENEW-5 are not reported). The `startDate` body is `400` with `errors["$.startDate"]` and no `ruleId`: the shape comes before the 409. The GET is `200`: `status` Cancelled, `cancellation` {T + 30, CANCEL-1c, 328.18}, one refund (Cheque, 328.18, the sale's `PAY-`), one term, one payment | | |
| 33 | RENEW-5 | H7 | Run H7, lines 1–3 (the sale, a null amount, a string amount) | The sale is `201` (start T + 22). Both renewals are `400` `application/problem+json`, `title` "One or more validation errors occurred.", `instance` /policies/QA-F4-SHAPE/renewals, a `traceId`, no `ruleId`: `{"amount":null}` → `errors.amount` = ["The Amount field is required."]; `{"amount":"401.16"}` → `errors["$.amount"]` = ["Expected a number."] | | |
| 34 | RENEW-4 | H7 | Run H7, lines 4–5 (no body, with and then without `Content-Type: application/json`) | Each is `400` `application/problem+json` (never `415` or `500`), the same title, `instance` /policies/QA-F4-SHAPE/renewals, a `traceId`, no `ruleId`, and `errors` with exactly one entry, `amount` = ["A request body with an amount is required."]. The key is `amount`, as in steps 12 and 33. The ed44a60 pre-run got `Amount` (capital A). d931d20 and b5ca22e fixed it, and the re-check at b5ca22e got `amount`: see the notes, candidate defect 1 (closed) | | |
| 35 | RENEW-5 | H7 | Run H7, lines 6–9 (0.01, 1000000.00, 0.00, the GET) | `0.01` → `422` with RENEW-1/2 alone (`windowStart` W+22, `windowEnd` E+22): one penny is not RENEW-5. `1000000.00` → `422` with RENEW-1/2 alone: the cap itself is not a `400`. `0.00` → `422`, `detail` "2 rules failed.", `violations` = [RENEW-1/2, RENEW-5 ("The renewal amount must be greater than 0.")]. The GET is `200`: `status` Pending, one term (T + 22 to E+22, 344.27), one DirectDebit payment, nothing added | | |
| 36 | GET-1 | H8 | Run H8 (all three lines) | `QA-F4-NONE` with `1.001` → `400`, `errors.amount` = ["The amount must be at most 1,000,000.00 with no more than 2 decimal places."], `instance` /policies/QA-F4-NONE/renewals: the shape comes before the 404. With `401.16` → `404` /problems/get-1, `title` "Policy not found", `detail` "No policy with reference 'QA-F4-NONE'.", `ruleId` GET-1, `reference` QA-F4-NONE. `?amount=5` on QA-F4-SHAPE → `400`, `errors.amount` = ["Unknown query parameter."], no `ruleId` | | |
| 37 | RENEW-3b | H5, H6 | Read the bodies of H5 and H6 again | No 422 or 409 body in H5 or H6 echoes the request's `paymentMethod`, and no RENEW-3b violation entry has a member besides `ruleId` and `detail` (A1/R2: the contract lists no RENEW-3b extension) | | |
| 38 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3 | — (the API console) | Look at the API console output for sections A to C | No `fail:` or `crit:` line: nothing went down the 500 path (`UnexpectedExceptionHandler`, logged at Error). One `warn: Microsoft.EntityFrameworkCore.Query[20504]` line (more than one collection `Include`, single query) may appear at the first lookup; it's a performance hint, not a fault. No policyholder name, date of birth, address or postcode appears anywhere in the console | | |

### D. Rows that need a fake clock or a race: covered by tests

Stop the API first (it locks the build output). These rows can't run live. A renewal window opens about 11 months
after a start, and SELL-2 lets a policy start at most 60 days ahead, so every success row, every edge of the window,
COULD-3 and RENEW-5 on their own, and every CANCEL-5 row need a `FakeTimeProvider`. The BST edge needs one too. The
concurrency 409 needs a second save at an exact point. Each command prints `total: n, failed: 0, succeeded: n, skipped: 0`.

| # | Rule | Covered by test | Command | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 39 | RENEW-1/2 | `RenewWindowTests` (Api.Tests, 8; start 2026-10-29, end 2027-10-28, window 2027-09-28 to 2027-10-28): `Renewal_31_days_before_the_end_is_rejected_with_422` (2027-09-27), `Renewal_30_days_before_the_end_is_accepted` (2027-09-28; term 2 2027-10-29 to 2028-10-28, 366 days, 446.83), `Renewal_at_23_30_UTC_in_BST_on_the_day_before_the_window_is_on_its_first_UK_day_and_accepted` (2027-09-27T23:30Z), `Renewal_on_the_day_of_sale_is_rejected_with_422`, `Renewal_on_the_end_date_is_accepted` (421.30 by Card), `Renewal_the_day_after_the_end_is_rejected_with_422` (2027-10-29, the policy reads back Expired), `Renewal_at_23_30_UTC_in_BST_on_the_end_date_is_on_the_next_UK_day_and_rejected` (2027-10-28T23:30Z), `Renewal_read_across_UK_midnight_is_checked_and_answered_on_one_UK_today`. Domain.Tests `RenewWindowTests` (8, including the second renewal's window measured from the latest term). Services.Tests `PolicyServiceRenewWindowTests` (5: BST and GMT at 23:30 UTC on both edges, the clock read once) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewWindowTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewWindowTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceRenewWindowTests"` | 8 passed, then 8, then 5. Both edges are inclusive, each side is its own test, and today is always the UK date, never the UTC date | | |
| 40 | RENEW-1/2 | `Renewal_read_across_UK_midnight_is_checked_and_answered_on_one_UK_today` (Api.Tests: the clock moves an hour on every read from 2027-10-28T22:30Z, the end date; the renewal is `201` and the body and GET show `currentTermNumber` 1, Active, not a second read on 2027-10-29; finding A1/R1). Services.Tests `Renew_reads_the_clock_once` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Renewal_read_across_UK_midnight_is_checked_and_answered_on_one_UK_today"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-method "*.Renew_reads_the_clock_once"` | 1 passed, then 1. One request has one UK today: the date the window checks is the date the response is built on | | |
| 41 | RENEW-3a | `RenewPaymentTests` (Api.Tests, 4): no method on a DirectDebit policy (538.46) → term 2 paid 557.09 by DirectDebit; no method on a Card policy (611.72) → 634.05 by Card; Card sent on a DirectDebit policy → 515.60 by Card; DirectDebit sent on a Card policy → 390.00 by DirectDebit. Each payment has its own generated reference, term 2 links to it, and term 1's payment is unchanged. Domain.Tests `RenewPaymentTests` (7, including the default taken from the term covering today, not the first). Services.Tests `PolicyServiceRenewPaymentTests` (4, including the reference from the generator and `Renewal_defaulting_to_a_Cheque_is_rejected_and_saves_nothing`) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewPaymentTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewPaymentTests"`, then `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceRenewPaymentTests"` | 4 passed, then 7, then 4. Each renewal's payment is for its term's amount (the F3 security carry-over: a refund can't exceed what was paid) | | |
| 42 | RENEW-3b | `RenewUnpaidTests` (Api.Tests, 5): without auto-renew and without a method, term 2 has `paymentReference` null and no payment is added (436.51); with Card (362.40) `422` RENEW-3b; `Renewal_without_auto_renew_with_a_payment_method_is_rejected_with_no_extension_members` (finding A1/R2); a Cheque sent is RENEW-3b alone, not COULD-3; a Cheque policy without auto-renew renews unpaid (468.12). Domain.Tests `RenewUnpaidTests` (5) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewUnpaidTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewUnpaidTests"` | 5 passed, then 5 | | |
| 43 | RENEW-4 | `RenewTermTests` (Api.Tests, 6): BuyToLet with two holders, sold 2026-11-30 (562.91, DirectDebit), renewed on 2027-11-10 for 588.04 by Card → term 2 2027-11-30 to 2028-11-29, same reference, `Location` /policies/RENEW4-APPEND; term 2 is current from its start date (`currentTermNumber` 2); a second renewal the same day is `422` with the latest term's window 2028-11-04 to 2028-12-04; term 3 2028-11-12 to 2029-11-11 (481.27, by term 2's DirectDebit); the lower-case route reference; the `404`. Domain.Tests `RenewTermTests` (5: a 366-day renewed term through 29 February, a term ending 29 February renewed from 1 March, a third term, the current term unchanged) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewTermTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewTermTests"` | 6 passed, then 5. Every success row asserts the 201 body and the GET read-back in full (`RenewedPolicyAssert`), with each date and amount a literal | | |
| 44 | RENEW-5 | `RenewAmountTests` (Api.Tests, 4): 0.01 accepted, 1,000,000.00 accepted, 0.00 `422` RENEW-5 alone with `amount` "0.00", −25.50 `422` with `amount` "-25.50". Domain.Tests `RenewAmountTests` (4, including 0 without auto-renew) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewAmountTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewAmountTests"` | 4 passed, then 4 | | |
| 45 | RENEW-4 | `RenewShapeTests` (Api.Tests, 10): amount missing, null, 3 dp, 1,000,000.01, as a string; an unknown and an integer method; `Renewal_with_a_startDate_as_an_unknown_field_is_rejected_with_400` (renamed by finding A1/R3); malformed JSON; no body | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewShapeTests"` | 10 passed. These are the only F4 tests that passed on the stub (gate.stub). `Renewal_with_no_body_is_rejected_with_400` pins the key: `errors` has exactly one entry, `amount`, compared case-sensitively (step 34; red d931d20, green b5ca22e) | | |
| 46 | RENEW-6 | `RenewCancelledTests` (Api.Tests, 2): cancelled from 2027-10-05 (CANCEL-1c, 14.89 to Card), renewed inside its window on 2027-09-25 → `409`, `cancellationDate` 2027-10-05, the refund kept, no term added; cancelled today, then amount 0.00 by Cheque today → `409` alone. Domain.Tests `RenewCancelledTests` (3) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewCancelledTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewCancelledTests"` | 2 passed, then 3. The first is the RENEW-6 row with no other rule broken, which can't run live | | |
| 47 | COULD-3 | `RenewChequeTests` (Api.Tests, 3), each inside the window: an explicit Cheque (472.30) → `422` COULD-3 alone with `paymentMethod` Cheque; the defaulted Cheque (523.48) → `422` COULD-3 alone, `paymentMethod` Cheque, and a `detail` asking for Card or DirectDebit; a Cheque policy renewed by Card → `201`, term 2 495.37 by Card. Domain.Tests `RenewChequeTests` (5) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewChequeTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewChequeTests"` | 3 passed, then 5 | | |
| 48 | RENEW-5 | `RenewMultipleViolationsTests` (Api.Tests, 4): today, without auto-renew, Card, 0.00 → [RENEW-1/2, RENEW-3b, RENEW-5]; today, by Cheque, −1.00 → [RENEW-1/2, RENEW-5, COULD-3]; inside the window (2027-11-02), 0.00 with a defaulted Cheque → [RENEW-5, COULD-3]; after the end (2027-11-05, Expired), without auto-renew, DirectDebit → [RENEW-1/2, RENEW-3b]. Domain.Tests `RenewMultipleViolationsTests` (4) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewMultipleViolationsTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.RenewMultipleViolationsTests"` | 4 passed, then 4. Every 422 rule is collected, in rule-ID order, and none returns early | | |
| 49 | CANCEL-5 | `CancelRenewedPolicyTests` (Api.Tests, 5): sold 2026-11-03 (487.39, DirectDebit), renewed for 512.66 by Card, cancelled 2027-10-20 → term 1 CANCEL-1c {365, 352, 13, 17.36}, term 2 CANCEL-1a {366, 0, 366, 512.66}, total 530.02, two refunds, each to its own payment's method; cancelled on term 2's start + 14 → term 1 {365, 365, 0, 0.00} and no refund for it, term 2 CANCEL-1b 548.27; on term 2's start + 15 → term 2 CANCEL-1c {366, 16, 350, 570.35}; a renewed term left unpaid (RENEW-3b) → term 2 UNPAID-TERM 0.00 and no refund, term 1 12.23; the quote of that case → 12.00, `quotedOn` 2027-11-08, nothing changed. Each reads back `status` Cancelled (or, for the quote, unchanged) with both terms. Domain.Tests `CancelRenewedPolicyTests` (6, including a renewed term refunded no more than its payment) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.CancelRenewedPolicyTests"`, then `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.CancelRenewedPolicyTests"` | 5 passed, then 6. The current term follows the normal rules, a future term is refunded in full, and no £0 refund is made | | |
| 50 | CONCURRENCY | `RenewConcurrencyTests` (Api.Tests, untraited): `Renewal_that_loses_a_race_answers_409_concurrency_and_stores_nothing`, `Renewal_retried_after_a_lost_race_is_accepted` (445.90; term 2 2027-11-15 to 2028-11-14) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.RenewConcurrencyTests"` | 2 passed. A renewal that loses the race is `409` `type` /problems/concurrency, `title` "The policy was changed by another request", `detail` "Retry the request.", no `ruleId`; the policy reads back with one term, and a retry is accepted | | |
| 51 | RENEW-1/2 | every test traited RENEW-1 (the opening edge) | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=RENEW-1"` | 18 passed: Api.Tests 7, Domain.Tests 8, Services.Tests 3 | | |
| 52 | RENEW-1/2 | every test traited RENEW-2 (the closing edge) | The step 51 command with `Rule=RENEW-2` | 12 passed: Api.Tests 5, Domain.Tests 4, Services.Tests 3 | | |
| 53 | RENEW-3a | every test traited RENEW-3a | The step 51 command with `Rule=RENEW-3a` | 24 passed: Api.Tests 9, Domain.Tests 11, Services.Tests 4 | | |
| 54 | RENEW-3b | every test traited RENEW-3b | The step 51 command with `Rule=RENEW-3b` | 17 passed: Api.Tests 9, Domain.Tests 8 | | |
| 55 | RENEW-4 | every test traited RENEW-4 | The step 51 command with `Rule=RENEW-4` | 13 passed: Api.Tests 8, Domain.Tests 5 | | |
| 56 | RENEW-5 | every test traited RENEW-5 | The step 51 command with `Rule=RENEW-5` | 19 passed: Api.Tests 12, Domain.Tests 7 | | |
| 57 | RENEW-6 | every test traited RENEW-6 | The step 51 command with `Rule=RENEW-6` | 5 passed: Api.Tests 2, Domain.Tests 3 | | |
| 58 | COULD-3 | every test traited COULD-3 | The step 51 command with `Rule=COULD-3` | 17 passed: Api.Tests 7, Domain.Tests 9, Services.Tests 1 | | |
| 59 | CANCEL-5 | every test traited CANCEL-5 | The step 51 command with `Rule=CANCEL-5` | 11 passed: Api.Tests 5, Domain.Tests 6 | | |
| 60 | GET-1 | every test traited GET-1 | The step 51 command with `Rule=GET-1` | 65 passed. F3 signed off 64; F4 adds `Renewal_of_an_unknown_reference_answers_404` | | |
| 61 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5 | `RuleCoverageTests` | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`, then open `tests/rule-coverage.pending.txt` | 11 passed. The pending file is empty (0 bytes): F3 signed off 9 pending IDs, and F4 took all 9 out. Every rule in `docs/requirements.md` now has a unit trait and an integration trait | | |
| 62 | RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5 | the whole suite | `dotnet test --solution Uinsure.Policies.slnx` | 637 passed, 0 failed, 0 skipped (F3 signed off 528) | | |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- How these expectations were checked. On 2026-10-19, between about 05:30 and 05:45 UTC (06:30 to 06:45 UK time, BST;
  UK today 2026-10-19, so `T` = 2026-10-19, `E` = 2027-10-18 and `W` = 2027-09-18), the tester ran these at HEAD
  b0169c9, whose `src/` and `tests/` match ed44a60. It ran the full suite (637 / 0 / 0), every command in section D with
  the counts given, and each per-rule trait filter. The per-project split was read from the trait attributes, and no
  F4 test is a `[Theory]`, so it matches every total. It also ran the API from the worktree on 5080
  (`Hosting environment: Development`, `/health/live` and `/health/ready` both `200`). With the API up, the Postman CLI
  ran `F4 Renew` (54 / 0 requests, 53 / 0 assertions), `F3 Cancel` (87 / 0, 82 / 0), `F2 Retrieve` (25 / 0, 26 / 0),
  `F1b Layered models` (10 / 0, 10 / 0), `F1 Sell` (75 / 0, 73 / 0), `F0b Walking skeleton` (2 / 0, 2 / 0) and the
  whole collection (253 / 0, 246 / 0). Step 24's diff was also run. The hand requests were sent with Git Bash `curl`
  under `QAV-F4-*` references, not this script's `QA-F4-*`, so yours stay free. Their statuses and bodies are the ones
  section C expects, except step 34's key (below). The console had 0 `fail:` and 0 `crit:` lines, one `warn:`
  (EF Core 20504), and no name, date of birth, address or postcode. At about 05:51 UTC the same day, the tester
  re-ran step 34 at HEAD b5ca22e (the API from the worktree on 5080, sold under `QAV-F4-SHAPE3`, start T + 22). Both
  body-less renewals answered `400` with `errors: { "amount": [...] }`, and the read-back was unchanged. The tester
  also ran `RenewShapeTests` (10 / 0). The harness doesn't let the tester run
  `powershell.exe`, so the shell setup and the PowerShell lines of H1–H8 haven't been run as written. If one
  misbehaves, note it here: that is a script defect, not an API defect. The Actual and Pass/Fail columns are yours.
- **Candidate defect 1 (step 34): closed in-task under t13, by red d931d20 and green b5ca22e.** At b5ca22e the
  body-less renewal's `400` keys its error `amount`. `PoliciesController.Renew` now adds the model error under the
  wire name `"amount"`, and `Renewal_with_no_body_is_rejected_with_400` asserts that `errors` has exactly the one key
  `amount`. It failed at d931d20 (expected `amount`, actual `Amount`) and passes at b5ca22e. The A1.3 reviewer
  confirmed the fix (verdict PASS). The trail has one gap. b5ca22e is a `fix(…)` commit with no `Fixes-Finding`
  trailer (A1.3/R2), because commit-msg has no id form for a QA finding. That's a harness gap, not a code defect.
  Step 34 now expects `amount` and step 45 records the pinned key. What was found at ed44a60, kept for the record:
  a renewal with no body at all answered `400` with
  `errors: { "Amount": ["A request body with an amount is required."] }`, keyed `Amount` with a capital A. Every other
  E6 shape error was keyed camelCase (`amount`, `$.amount`, `$.paymentMethod`, `$.startDate`), and the test helper
  says keys are written "as the contract writes it (camelCase JSON names)". The cause was in
  `src/Uinsure.Policies.Api/Controllers/PoliciesController.cs`.
  `ModelState.AddModelError(nameof(RenewPolicyRequest.Amount), …)` added the key `Amount`, and the action's own
  `ValidationProblem()` builds the response through the problem-details factory, not through
  `ValidationProblems.FromModelState`. The A1 reviewer considered this and withdrew it, reasoning that the response
  goes through `InvalidModelStateResponseFactory`. Measured over HTTP, it didn't. At ed44a60,
  `Renewal_with_no_body_is_rejected_with_400` only asserted that `errors` was non-empty, so no test pinned the key. It
  was cosmetic: the status, the title, the absence of `ruleId` and the message were all right, and nothing leaked.
  b5ca22e spells out the key. Routing the no-body case through the same factory as the other 400s would also work.
  The A1.3 reviewer called that optional, not a finding.
- For your ruling, not raised as a finding: the RENEW-1/2, RENEW-6 and COULD-3 `detail` texts differ from the
  contract's examples. The API says "A policy can be renewed from W to E." (the contract's example also names today),
  "The policy was cancelled on T + 30, so it cannot be renewed." (the example names the reference and says "with effect
  from"), and, for both the sent and the defaulted Cheque, "An auto-renewing policy cannot be renewed by Cheque. Use
  Card or DirectDebit.". The contract gives the two COULD-3 cases different example texts. The requirement asks only
  that the detail "asks for another method", which this text does, and the `windowStart`, `windowEnd` and
  `cancellationDate` extensions carry the dates. The contract marks these texts as examples, and F3's 409 texts
  differed in the same way.
- For your ruling: with two or more violations, the top level of a 422 carries no rule extension (no `windowStart` or
  `windowEnd` in steps 3–8, 28, 29 and 31), and a `violations[]` entry carries only `ruleId` and `detail`. The
  contract defines the extensions only for the single-violation case ("the rule-specific extensions sit at the top
  level"), so this reads as conforming. One consequence: RENEW-5's `amount` and COULD-3's `paymentMethod` extensions
  can't be seen live, because a live renewal always breaks RENEW-1/2 too. Steps 44 and 47 show them.
- For your ruling: on a policy that does not auto-renew, `"paymentMethod": null` is treated as no method (step 31), so
  it is not RENEW-3b. The rule says a request that "sends a `paymentMethod`" gets 422. A JSON null sends none, which
  matches the nullable contract field. No test pins this.
- Carried over from F1, not new in F4: enum names are read case-insensitively (`"paymentMethod": "card"` is accepted
  on E6 as on E1, where `"type": "household"` is too), while integers are refused (step 16). And a JSON value that
  can't be converted gives a System.Text.Json message naming the internal type. On E6,
  `"paymentMethod": ""` gives "The JSON value could not be converted to
  ``System.Nullable`1[Uinsure.Policies.Api.Contracts.PaymentMethod]``…", like the Sell and Claim messages noted in F1
  and F3. Neither leaks data.
- The A1.2/R1 reviewer finding (P2, non-blocking, PASS verdict) is about the trail, not behaviour. The tester's
  re-targeting of `PolicyServiceRenewWindowTests.cs` and `PolicyServiceRenewPaymentTests.cs` landed inside builder
  green ed44a60, not in a commit of its own. Step 39's and step 41's Services counts are measured at HEAD.
- Run Postman and the hand steps away from UK midnight. Both compute UK today themselves, as the API does, so a run
  that crosses midnight can sell on one date and renew or cancel on the next, and every W and E would move by a day.
  The clocks go back on 2026-10-25. The PowerShell setup uses the Europe/London zone (`GMT Standard Time`), so it
  follows the change. Step 32's refund (328.18) assumes a 365-day term, which holds for any `T` up to 2027-02-28.

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
| Verdict | ☐ Reject (findings above)  ☐ Accept F4 |
| Signature | |
