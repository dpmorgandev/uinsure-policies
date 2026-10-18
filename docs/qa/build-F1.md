# QA script — build / F1 — Sell (+ bare GET)

Rule IDs: SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7
Branch: feature/build/F1   Worktree: .claude/worktrees/build-F1
Commit under test: 6d55f06 (the code HEAD when this script was written; the script's own commit adds only this file. Record the SHA you test in the sign-off)
API URL: http://localhost:5080 and https://localhost:7080   Environment: local (`tests/postman/local.postman_environment.json`)

Commands are for PowerShell, run from the worktree root. Steps are tagged with the rule they check, `[SELL-n]`.
400-shape rows that guard no numbered rule are tagged `[SHAPE]`. The D1 transport steps are tagged with their
D1 finding id (`[D1-I1]` to `[D1-I7]`, from `docs/trace/build/F1/findings/D1.md`): I1 HTTPS-only and HSTS outside
Development, I2 request body limit, I3 a bad request keeps its own status, I4 no redundant `partial class Program`,
I5 forwarded headers trusted from private networks only, I6 rate limiter, I7 the https launch profile.

## Preconditions

- [ ] `dotnet test --solution Uinsure.Policies.slnx` is green in the worktree (passed / failed / skipped: __ / __ / __). Expected: 277 / 0 / 0.
- [ ] Ports 5080 and 7080 are free (`netstat -ano | findstr ":5080 :7080"` prints no LISTENING line). A running API also locks the build output, so stop it before any `dotnet build` or `dotnet test`.
- [ ] The dev certificate is trusted: `dotnet dev-certs https --check --trust` prints `A trusted certificate was found`.
- [ ] Start from an empty database, so the fixed `QA-F1-*` references in the hand steps are free. With the API stopped: `Remove-Item src/Uinsure.Policies.Api/policies.development.db*`. Start-up recreates it (`EnsureCreated`).
- [ ] You are using the Postman collection and the `local` environment from this branch (`tests/postman/`).

### Starting the API

`dotnet run --project src/Uinsure.Policies.Api`. With no `--launch-profile`, `dotnet run` takes the first profile,
which is now `https`. The console shows `Now listening on: https://localhost:7080`, `Now listening on: http://localhost:5080`
and `Hosting environment: Development`. (`--launch-profile http` gives 5080 only. In VS 2026 both profiles are in the ▶ drop-down.)

### Shell setup for the hand steps (H1–H6)

Paste this once into the PowerShell window you use for the hand steps. UK today comes from Europe/London, as in the API.

```powershell
$ukToday   = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'GMT Standard Time').Date
$start     = $ukToday.AddDays(22).ToString('yyyy-MM-dd')
$yesterday = $ukToday.AddDays(-1).ToString('yyyy-MM-dd')
$under16   = $ukToday.AddDays(22).AddYears(-16).AddDays(1).ToString('yyyy-MM-dd')   # 16th birthday the day after $start
function Sell([string]$json) { $json | curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-" }
```

The JSON is piped to curl so that PowerShell does not strip its quotes. If `curl.exe` reports a certificate
revocation error on an `https://` URL, add `--ssl-no-revoke`.

### Hand requests

```powershell
# H1 [SELL-1] the original sale
Sell ('{"reference":"QA-F1-DUP","type":"Household","startDate":"' + $start + '","amount":527.18,"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}')

# H2 [SELL-1] the same reference, lower case with spaces, and a body that also breaks SELL-2 (yesterday) and SELL-7 (amount 0)
Sell ('{"reference":" qa-f1-dup ","type":"Household","startDate":"' + $yesterday + '","amount":0,"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}')
curl.exe -s -i http://localhost:5080/policies/QA-F1-DUP

# H3 [SELL-5] a date of birth in 9984 (an extreme bindable date)
Sell ('{"reference":"QA-F1-9984","type":"Household","startDate":"' + $start + '","amount":527.18,"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"9984-01-01"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}')
curl.exe -s -i http://localhost:5080/policies/QA-F1-9984

# H4 [SELL-5] the second of two policyholders turns 16 the day after the start date
Sell ('{"reference":"QA-F1-IDX2","type":"Household","startDate":"' + $start + '","amount":527.18,"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"Katherine","lastName":"Johnson","dateOfBirth":"1981-08-26"},{"firstName":"Ada","lastName":"Lovelace","dateOfBirth":"' + $under16 + '"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}')

# H5 [SHAPE] a first name of 101 characters (the column holds 100)
Sell ('{"reference":"QA-F1-NAME","type":"Household","startDate":"' + $start + '","amount":527.18,"autoRenew":true,"paymentMethod":"Card","policyholders":[{"firstName":"' + ('K' * 101) + '","lastName":"Johnson","dateOfBirth":"1981-08-26"}],"property":{"addressLine1":"48 Quay St","postcode":"M3 4LZ"}}')

# H6 [D1-I2] [D1-I3] a 70,000-byte body (the limit is 65,536)
('A' * 70000) | curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-"
```

## Steps

### A. Postman, real-time rows (API running in Development, from "Starting the API")

Run each request from the Postman app (folder `F1 Sell`, environment `local`), or run the whole folder at once in
step 37. Every request makes its own reference and computes its dates from the real UK today. Every 201 row reads
the policy back by `GET` and checks every value sold. Every rejected row checks `GET` → 404, so nothing was stored.
Every 400 row has `title` "One or more validation errors occurred.", an `errors` map and a `traceId`, and no `ruleId`.
Every 409 and 422 row has `instance` `/policies` and a `traceId`.

| # | Rule | Postman request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | SELL-1 | SELL-1 · new reference → 201 | Send | `201`, `Location: /policies/{reference}`. GET → `200` with the same reference, BuyToLet, `412.34`, two policyholders and the address, read back as sold | | |
| 2 | SELL-1 | SELL-1 · lower-case reference with spaces is normalised → 201 | Send (`"  f1-…  "`) | `201`, `Location` holds the upper-cased reference. GET by the upper-case and by the lower-case reference both → `200` with the upper-cased `reference` | | |
| 3 | SELL-1 | SELL-1 · reference of 20 characters → 201 | Send | `201` and the GET round trip | | |
| 4 | SELL-2 | SELL-2 · start today → 201 | Send | `201`. GET: `startDate` = today, `endDate` = today + 1 year − 1 day | | |
| 5 | SELL-2 | SELL-2 · start today+60 → 201 | Send | `201`. GET: `startDate` = today + 60 | | |
| 6 | SELL-3 | SELL-3 · end = start + 1 year − 1 day → 201 | Send (start today + 22; the body has no end date) | `201`. GET: `endDate` and `terms[0].endDate` = start + 1 year − 1 day, one term with `termNumber` 1 | | |
| 7 | SELL-4 | SELL-4 · 3 policyholders → 201 | Send | `201`. GET: `policyholders` has 3 entries, in the order sent | | |
| 8 | SELL-5 | SELL-5 · 16th birthday on the start date → 201 | Send (date of birth = start − 16 years) | `201` and the GET round trip | | |
| 9 | SELL-6 | SELL-6 · payment for the full amount → 201 | Send (Card, `389.5`) | `201`. GET: one payment, `type` Card, `amount` 389.5, a non-empty `reference` (`PAY-…`), and `terms[0].paymentReference` equal to it | | |
| 10 | SELL-7 | SELL-7 · amount 0.01 → 201 | Send | `201`. GET: `amount` 0.01 | | |
| 11 | SHAPE | SHAPE · postcode of 8 characters → 201 | Send | `201` and the GET round trip | | |
| 12 | SELL-1 | SELL-1 · duplicate reference → 409 | Send (the pre-request script sells the original first) | `409`, `type` /problems/sell-1, `title` "Policy reference already exists", `ruleId` SELL-1, `reference` = the stored reference, no `violations`. GET: the original (BuyToLet, 412.34) is unchanged | | |
| 13 | SELL-1 | SELL-1 · duplicate differing only in case → 409 | Send | `409`, /problems/sell-1, `ruleId` SELL-1, `reference` upper-cased | | |
| 14 | SELL-1 | SELL-1 · reference of 2 characters → 400 | Send (`S1`) | `400`, `errors.reference`, no `ruleId` | | |
| 15 | SELL-1 | SELL-1 · reference of 21 characters → 400 | Send | `400`, `errors.reference` | | |
| 16 | SELL-1 | SELL-1 · reference with an underscore → 400 | Send (`SELL1_UNDERSCORE`) | `400`, `errors.reference` | | |
| 17 | SELL-2 | SELL-2 · start today+61 → 422 | Send | `422`, `type` /problems/sell-2, `title` "Start date outside the sale window", `ruleId` SELL-2, `violations` = [SELL-2], `earliest` = today, `latest` = today + 60 | | |
| 18 | SELL-2 | SELL-2 · start yesterday → 422 | Send | `422`, /problems/sell-2, `violations` = [SELL-2], `earliest` = today, `latest` = today + 60 | | |
| 19 | SELL-2, SELL-4, SELL-5, SELL-7 | SELL-2 · start today+61, 4 policyholders, one under 16, amount 0 → 422 | Send | `422`, `ruleId` SELL-2 (the first in rule order), `detail` "4 rules failed.", `violations[].ruleId` = [SELL-2, SELL-4, SELL-5, SELL-7] | | |
| 20 | SELL-3 | SELL-3 · endDate sent → 400 | Send (body carries `endDate`) | `400`, `errors["$.endDate"]` (unknown member) | | |
| 21 | SELL-4 | SELL-4 · 0 policyholders → 422 | Send | `422`, `type` /problems/sell-4, `title` "Wrong number of policyholders", `ruleId` SELL-4, `violations` = [SELL-4], `count` 0 | | |
| 22 | SELL-4 | SELL-4 · 4 policyholders → 422 | Send | `422`, /problems/sell-4, `violations` = [SELL-4], `count` 4 | | |
| 23 | SELL-5 | SELL-5 · 16th birthday the day after the start date → 422 | Send | `422`, `type` /problems/sell-5, `title` "Policyholder under 16", `ruleId` SELL-5, `violations` = [SELL-5], `index` 1 | | |
| 24 | SELL-6 | SELL-6 · paymentMethod missing → 400 | Send | `400`, `errors.paymentMethod`, no `ruleId` | | |
| 25 | SELL-7 | SELL-7 · amount 0 → 422 | Send | `422`, `type` /problems/sell-7, `title` "Policy amount must be greater than zero", `ruleId` SELL-7, `violations` = [SELL-7], `amount` 0 | | |
| 26 | SELL-7 | SELL-7 · amount negative → 422 | Send (`-1`) | `422`, /problems/sell-7, `violations` = [SELL-7], `amount` -1 | | |
| 27 | SHAPE | SHAPE · malformed JSON → 400 | Send | `400`, a non-empty `errors` map | | |
| 28 | SHAPE | SHAPE · unknown field → 400 | Send (`colour`) | `400`, `errors["$.colour"]` | | |
| 29 | SHAPE | SHAPE · hasClaims sent → 400 | Send | `400`, `errors["$.hasClaims"]` | | |
| 30 | SHAPE | SHAPE · startDate missing → 400 | Send | `400`, `errors.startDate` | | |
| 31 | SHAPE | SHAPE · startDate not yyyy-MM-dd → 400 | Send (`01/11/2026`) | `400`, `errors["$.startDate"]` | | |
| 32 | SHAPE | SHAPE · unknown policy type → 400 | Send (`Flat`) | `400`, `errors["$.type"]` | | |
| 33 | SHAPE | SHAPE · integer policy type → 400 | Send (`0`) | `400`, `errors["$.type"]` | | |
| 34 | SHAPE | SHAPE · integer payment method → 400 | Send (`1`) | `400`, `errors["$.paymentMethod"]` | | |
| 35 | SHAPE | SHAPE · postcode of 9 characters → 400 | Send | `400`, `errors["property.postcode"]` | | |
| 36 | SHAPE | SHAPE · address line 1 missing → 400 / SHAPE · policyholder date of birth missing → 400 | Send both | `400`, `errors["property.addressLine1"]`; then `400`, `errors["policyholders[0].dateOfBirth"]` | | |
| 37 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | whole folder `F1 Sell` | `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "F1 Sell"` | Every line is `Pass`. The summary shows requests 75 / 0 failed and assertions 73 / 0 failed (the 35 requests plus the extra sales and GETs their scripts send). The closing "Postman credentials are required…" line is about cloud publishing only | | |
| 38 | D1-I7, SELL-1 | whole folder over HTTPS | The same command with `--env-var "baseUrl=https://localhost:7080"` added | The same totals: requests 75 / 0, assertions 73 / 0, every URL `https://localhost:7080/…` | | |

### B. Hand requests (same API; the shell setup and H1–H6 above)

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 39 | SELL-1 | H1 | Run H1 | `201`, `Location: /policies/QA-F1-DUP`, `endDate` = start + 1 year − 1 day, one payment `PAY-…` | | |
| 40 | SELL-1 | H2 | Run H2 (both lines) | The POST gives `409` with `type` /problems/sell-1, `ruleId` SELL-1, `reference` QA-F1-DUP, and **no** `violations`. The duplicate wins over the SELL-2 and SELL-7 breaks; it is returned alone. The GET gives `200` with `amount` 527.18 and `startDate` = $start, so the original is unchanged | | |
| 41 | SELL-5 | H3 | Run H3 (both lines) | `422`, `type` /problems/sell-5, `ruleId` SELL-5, `index` 1, `violations` = [SELL-5]. Never a 500. The GET gives `404` /problems/get-1 | | |
| 42 | SELL-5 | H4 | Run H4 | `422`, /problems/sell-5, `index` 2, `detail` "Policyholder 2 must be 16 or older on the start date." | | |
| 43 | SHAPE | H5 | Run H5 | `400`, `errors["policyholders[0].firstName"]` (maximum length 100), no `ruleId` | | |
| 44 | D1-I2, D1-I3 | H6 | Run H6 | `413 Payload Too Large`, `application/problem+json`, `title` "Request body too large.", `detail` "The request body is larger than this API accepts.", a `traceId`, and no `ruleId`. Neither the byte limit nor the exception message appears | | |
| 45 | D1-I7 | — | `curl.exe -s -i https://localhost:7080/health/live` | `200`, body `Healthy`. Development sends no `Strict-Transport-Security` header | | |

### C. Rate limiter, by hand [D1-I6]

The config key is `RateLimiting:Writes:PermitLimit` (`src/Uinsure.Policies.Api/appsettings.json`: Global 100 and
Writes 10 per minute; `appsettings.Development.json` raises them to 10000 and 1000). A command-line argument overrides both files.

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 46 | D1-I6 | — | Stop the API. Start it with `dotnet run --project src/Uinsure.Policies.Api -- --RateLimiting:Writes:PermitLimit=2`. Within one minute, run `1..3 \| % { '{}' \| curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-" }` | The first two are `400` (an empty body is a shape error, and it still spends a write permit, because every POST counts). The third is `429 Too Many Requests` with a `Retry-After` header of 60 seconds or less, `type` /problems/rate-limited, `title` "Too many requests.", a fixed `detail`, a `traceId` and no `ruleId`. It comes back at once (no queue) | | |
| 47 | D1-I6 | — | Still inside the minute: `curl.exe -s -i http://localhost:5080/policies/QA-F1-DUP`, `curl.exe -s -i http://localhost:5080/health/live`, then `'{}' \| curl.exe -s -i -X POST https://localhost:7080/policies -H "Content-Type: application/json" --data-binary "@-"` | The GET is `200` (GETs are under the global limit only, 10000 in Development). Health is `200` (exempt). The HTTPS POST is also `429`: the bucket is per client IP, not per port | | |

### D. Outside Development, by hand [D1-I1] [D1-I5]

| # | Rule | Request | Steps / input | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 48 | D1-I1 | — | Stop the API. Start it outside Development on both ports: `dotnet run --project src/Uinsure.Policies.Api --no-launch-profile -- --environment Production --urls "https://localhost:7080;http://localhost:5080" --Database:Provider Sqlite --ConnectionStrings:Policies "Data Source=policies.qa-production.db"` | The console shows both `Now listening on` lines and `Hosting environment: Production`. (The Database settings are needed because only `appsettings.Development.json` sets them.) | | |
| 49 | D1-I1 | — | `curl.exe -s -i http://localhost:5080/policies/QA-F1-DUP`, then `'{}' \| curl.exe -s -i -X POST http://localhost:5080/policies -H "Content-Type: application/json" --data-binary "@-"` | Both are `400` with `type` /problems/https-required, `title` "HTTPS is required.", `detail` "This API is served over HTTPS only. Repeat the request using https.", and no `ruleId`. It is a rejection, not a redirect (no `Location`). The POST is rejected before the controller runs, so there are no `errors` | | |
| 50 | D1-I1 | — | `curl.exe -s -i http://localhost:5080/health/live` and `curl.exe -s -i http://localhost:5080/health/ready` | Both `200` `Healthy` over plain HTTP: `/health/*` is exempt | | |
| 51 | D1-I1 | — | `curl.exe -s -i https://localhost:7080/policies/QA-F1-DUP` | `404` /problems/get-1 (this is a fresh, empty database file). HTTPS reaches the controller | | |
| 52 | D1-I1 | — | `curl.exe -s -i https://localhost:7080/health/live`, then `curl.exe -s -i -H "Host: policies.example.test" https://localhost:7080/health/live` | The first has **no** `Strict-Transport-Security` header: ASP.NET Core's HSTS leaves out `localhost`, `127.0.0.1` and `[::1]` by default. The second, with a real-looking host name, has `Strict-Transport-Security: max-age=2592000` | | |
| 53 | D1-I5 | — | `curl.exe -s -i -H "X-Forwarded-Proto: https" http://localhost:5080/policies/QA-F1-DUP` | Still `400` /problems/https-required. Loopback is not a trusted proxy (only 10.0.0.0/8, 172.16.0.0/12 and 192.168.0.0/16 are), so the header is ignored | | |
| 54 | D1-I1, D1-I2 | — | Over HTTPS this time: `('A' * 70000) \| curl.exe -s -i -X POST https://localhost:7080/policies -H "Content-Type: application/json" --data-binary "@-"` | `413` with the same fixed title and detail as step 44. Production shows no more than Development did | | |
| 55 | — | — | Stop the API. Run `Remove-Item src/Uinsure.Policies.Api/policies.qa-production.db*` and `git status --short` | The QA database is gone, and `git status` shows nothing under `src/` (`*.db` files are gitignored either way) | | |
| 56 | D1-I4 | — | `Select-String -Path src/Uinsure.Policies.Api/Program.cs -Pattern 'partial class Program'` | No match. `WebApplicationFactory<Program>` works without it (SDK 10) | | |

### E. Rows that need a fake clock or an injected race: covered by tests

Run these with the API stopped.

| # | Rule | Covered by test | Command | Expected | Actual | Pass/Fail |
|---|---|---|---|---|---|---|
| 57 | SELL-3 | `Start_on_29_February_2028_ends_on_28_February_2029`, `Start_on_1_March_2027_ends_on_29_February_2028` (Api.Tests, `SellTermLengthTests`, clock moved to 2028-01-15 and 2027-01-15) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Start_on_29_February_2028_ends_on_28_February_2029" --filter-method "*.Start_on_1_March_2027_ends_on_29_February_2028"` | 2 passed. A 29 Feb start can't be sold within 60 days of a real today until January 2028 | | |
| 58 | SELL-3 | `TermTests` and `SellTermLengthTests` in Domain.Tests (29 Feb 2028 → 28 Feb 2029, 28 Feb 2028 → 27 Feb 2029, 1 Mar 2027 → 29 Feb 2028) | `dotnet test --project tests/Uinsure.Policies.Domain.Tests -- --filter-class "Uinsure.Policies.Domain.Tests.TermTests" --filter-class "Uinsure.Policies.Domain.Tests.SellTermLengthTests"` | 7 passed | | |
| 59 | SELL-2 | `Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past`, `Start_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted`, `Start_date_of_the_UTC_date_at_23_30_UTC_in_GMT_is_accepted_as_today` (Api.Tests, `SellStartDateWindowTests`) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-method "*.Start_date_of_the_UTC_date_at_23_30_UTC_in_BST_is_rejected_as_past" --filter-method "*.Start_date_of_the_UK_date_at_23_30_UTC_in_BST_is_accepted" --filter-method "*.Start_date_of_the_UTC_date_at_23_30_UTC_in_GMT_is_accepted_as_today"` | 3 passed. At 2027-06-30T23:30Z, UK today is 2027-07-01, so a 2027-06-30 start is a 422 SELL-2. At 2027-01-15T23:30Z (GMT) a 2027-01-15 start is accepted | | |
| 60 | SELL-2 | `UkCalendarTests` and `PolicyServiceSellStartDateTests` (Services.Tests: BST/GMT, both clock-change days, day 60/61 at 23:30 UTC, the clock read once per sale) | `dotnet test --project tests/Uinsure.Policies.Services.Tests -- --filter-class "Uinsure.Policies.Services.Tests.UkCalendarTests" --filter-class "Uinsure.Policies.Services.Tests.PolicyServiceSellStartDateTests"` | 19 passed | | |
| 61 | SELL-1 | `Duplicate_reference_that_races_past_the_service_check_is_rejected_with_409_by_the_unique_index_and_the_original_is_unchanged`, `Duplicate_reference_that_races_past_the_service_check_gets_the_same_SELL_1_body_as_the_service_check` (`SellDuplicateRaceTests`) | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.SellDuplicateRaceTests"` | 2 passed. Two sales can't be raced by hand; the factory makes the service check miss, so the unique index answers. Its 409 body is the same SELL-1 body as step 12 | | |
| 62 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | every test traited with the rule, across Domain, Services and Api tests | `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=SELL-1"`, then the same for SELL-2 … SELL-7 | Passed, with no failures: SELL-1 27, SELL-2 26, SELL-3 13, SELL-4 11, SELL-5 16, SELL-6 18, SELL-7 10 | | |
| 63 | SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7 | `RuleCoverageTests` | `dotnet test --project tests/Uinsure.Policies.Architecture.Tests -- --filter-class "Uinsure.Policies.Architecture.Tests.RuleCoverageTests"`, then open `tests/rule-coverage.pending.txt` | 11 passed. The pending file has 23 IDs and no SELL ID: each SELL rule has a unit trait and an integration trait | | |
| 64 | D1-I1, D1-I2, D1-I3, D1-I5, D1-I6 | the transport classes: `HttpsTransportTests`, `RateLimitingTests`, `DefaultRateLimitsTests`, `ForwardedHeadersTests`, `KestrelRequestBodyLimitTests`, `RequestBodyLimitTests`, `BadRequestExceptionTests`, `DevelopmentBadRequestExceptionTests` | `dotnet test --project tests/Uinsure.Policies.Api.Tests -- --filter-class "Uinsure.Policies.Api.Tests.HttpsTransportTests" --filter-class "Uinsure.Policies.Api.Tests.RateLimitingTests" --filter-class "Uinsure.Policies.Api.Tests.DefaultRateLimitsTests" --filter-class "Uinsure.Policies.Api.Tests.ForwardedHeadersTests" --filter-class "Uinsure.Policies.Api.Tests.KestrelRequestBodyLimitTests" --filter-class "Uinsure.Policies.Api.Tests.RequestBodyLimitTests" --filter-class "Uinsure.Policies.Api.Tests.BadRequestExceptionTests" --filter-class "Uinsure.Policies.Api.Tests.DevelopmentBadRequestExceptionTests"` | 47 passed. These cover what the hand steps can't: the 408 and 400 bad-request statuses, the default 10 writes and 100 global per minute, the global-limit 429, forwarded HTTPS from a private-network proxy, and the client IP never being logged | | |

## Exploratory notes / defects

Each defect becomes a finding on this node's failure route, not a /plan-bug.

- Found while preparing this script, for your ruling (not raised as a finding): HSTS is not sent to `localhost`
  (step 52). That is the ASP.NET Core default (`HstsOptions.ExcludedHosts`), and the tests use the host
  `policies.example.test`, which is why they see the header. App Service serves a real host name, so it applies there.
- Step 46: a 400 shape error spends a write permit. That follows the rule that every POST carries the `writes` policy.
- The GET body has no `status` and no current-term fields. Finding A1/I1 removed them from F1 on purpose; they land in F2 (GET-1).
- How these expectations were checked: on 2026-10-18 the tester ran every request in sections A–D against 6d55f06 with
  Git Bash `curl` and the Postman CLI (both folder runs 75 / 0 and 73 / 0; the whole collection 77 / 0 and 75 / 0), and
  every command in section E. The harness doesn't let the tester run `powershell.exe`, so the PowerShell
  wrappers (the shell setup, `Sell`, the `('A' * 70000)` pipe and `1..3 | % {…}`) have not been run as written. If one
  misbehaves, note it here. That is a script defect, not an API defect.

## Sign-off

| | |
|---|---|
| Tester | David Morgan |
| Date | |
| Commit signed off | |
| `dotnet test` result | __ / __ / __ (total / failed / skipped) |
| Postman CLI result | requests __ / failed __; assertions __ / failed __ (folder `F1 Sell`, port 5080) |
| Steps passed | __ of 64 |
| Defects raised (finding ids) | |
| Verdict | ☐ Accept F1  ☐ Reject (findings above) |
| Signature | |
